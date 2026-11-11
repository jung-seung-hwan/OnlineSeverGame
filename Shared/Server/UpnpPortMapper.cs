using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace OnlineSeverGame.Server
{
    /// <summary>Outcome of <see cref="UpnpPortMapper.TryMap"/>.</summary>
    public sealed class UpnpResult
    {
        /// <summary>True when the router accepted AddPortMapping (HTTP 200).</summary>
        public bool Success;
        /// <summary>The router's WAN address from GetExternalIPAddress; may be empty (or private, behind a second NAT).</summary>
        public string ExternalIp;
        /// <summary>The LAN address the port was mapped to.</summary>
        public string LocalIp;
        /// <summary>Short English reason when <see cref="Success"/> is false; null on success.</summary>
        public string Error;
    }

    /// <summary>
    /// Hand-written UPnP IGD client: finds the home router with SSDP, reads its description XML and asks its
    /// WANIPConnection / WANPPPConnection service to forward TCP port N to this PC. Everything is blocking with
    /// timeouts and nothing throws, so it can run on a Unity background thread or in the console server.
    /// </summary>
    public sealed class UpnpPortMapper
    {
        internal const string ServiceWanIp = "urn:schemas-upnp-org:service:WANIPConnection:";
        internal const string ServiceWanPpp = "urn:schemas-upnp-org:service:WANPPPConnection:";

        private static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
        };

        private const int HttpTimeoutMs = 3000;
        private const int UnmapTimeoutMs = 2000;
        private const int FallbackLeaseSeconds = 7200;

        private readonly int port;
        private readonly string description;
        private readonly object gate = new object();

        // The mapping made by the last successful TryMap (null when there is none).
        private UpnpService mappedService;

        public UpnpPortMapper(int port, string description)
        {
            this.port = port;
            this.description = string.IsNullOrEmpty(description) ? "OnlineSeverGame" : description;
        }

        /// <summary>
        /// Blocking (up to ~timeoutMs for discovery + a few seconds for HTTP). Never throws; failures go to
        /// UpnpResult.Error (short English text).
        /// </summary>
        public UpnpResult TryMap(int timeoutMs = 3000)
        {
            UpnpResult result = new UpnpResult();
            try
            {
                if (port < 1 || port > 65535)
                {
                    result.Error = "Invalid port " + port;
                    return result;
                }

                string lanIp = LocalAddress.GetLanIPv4();
                result.LocalIp = lanIp;

                List<string> locations = Discover(lanIp, Math.Max(500, timeoutMs));
                if (locations.Count == 0)
                {
                    result.Error = "No UPnP router found (UPnP may be off on the router)";
                    return result;
                }

                string lastError = null;
                foreach (string location in locations)
                {
                    List<UpnpService> services;
                    try
                    {
                        HttpResponse desc = Http(location, "GET", null, null, HttpTimeoutMs);
                        if (desc.Status != 200)
                        {
                            lastError = "Router description returned HTTP " + desc.Status;
                            continue;
                        }
                        services = FindWanServices(desc.Body, location);
                    }
                    catch (Exception e)
                    {
                        lastError = "Router description failed: " + e.Message;
                        continue;
                    }

                    if (services.Count == 0)
                    {
                        lastError = "Router has no WANIPConnection/WANPPPConnection service";
                        continue;
                    }

                    foreach (UpnpService service in services)
                    {
                        string localIp = LocalIpToward(service.ControlUrl) ?? lanIp;
                        if (string.IsNullOrEmpty(localIp))
                        {
                            lastError = "No LAN IPv4 address";
                            continue;
                        }

                        string error = MapOn(service, localIp, out string externalIp);
                        if (error == null)
                        {
                            lock (gate)
                            {
                                mappedService = service;
                            }
                            result.Success = true;
                            result.ExternalIp = externalIp ?? "";
                            result.LocalIp = localIp;
                            result.Error = null;
                            return result;
                        }

                        lastError = error;
                        result.LocalIp = localIp;
                    }
                }

                result.Error = lastError ?? "UPnP mapping failed";
                return result;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Error = "UPnP error: " + e.Message;
                return result;
            }
        }

        /// <summary>Removes the mapping made by TryMap, if any. Blocking, never throws, short timeout (~2 s).</summary>
        public void Unmap()
        {
            UpnpService service;
            lock (gate)
            {
                service = mappedService;
                mappedService = null;
            }

            if (service == null)
            {
                return;
            }

            try
            {
                Soap(service, "DeletePortMapping", DeleteArgs(), UnmapTimeoutMs);
            }
            catch (Exception)
            {
                // Best effort: the router may be gone; a lease-0 mapping then stays until the router reboots.
            }
        }

        // ---- mapping ----

        /// <summary>Returns null on success, otherwise a short error.</summary>
        private string MapOn(UpnpService service, string localIp, out string externalIp)
        {
            externalIp = null;
            try
            {
                HttpResponse ext = Soap(service, "GetExternalIPAddress", new string[0], HttpTimeoutMs);
                if (ext.Status == 200)
                {
                    externalIp = ParseSoapValue(ext.Body, "NewExternalIPAddress");
                }
            }
            catch (Exception)
            {
                // The external IP is informational; still try to map.
            }

            HttpResponse add;
            try
            {
                add = Add(service, localIp, 0);
                if (add.Status == 200)
                {
                    return null;
                }

                int code = ParseSoapErrorCode(add.Body, out string _);
                if (code == 725 || code == 724)
                {
                    // 725 OnlyPermanentLeasesSupported is meant for nonzero leases, but some routers reject 0 with it
                    // (or with 724); retry with a finite lease.
                    add = Add(service, localIp, FallbackLeaseSeconds);
                    if (add.Status == 200)
                    {
                        return null;
                    }
                }
                else if (code == 718 && IsOurOwnMapping(service, localIp))
                {
                    // A mapping left behind by an earlier run of this PC (crash, lease 0): replace it.
                    Soap(service, "DeletePortMapping", DeleteArgs(), HttpTimeoutMs);
                    add = Add(service, localIp, 0);
                    if (add.Status == 200)
                    {
                        return null;
                    }
                }
            }
            catch (Exception e)
            {
                return "AddPortMapping failed: " + e.Message;
            }

            return DescribeFailure("AddPortMapping", add);
        }

        private HttpResponse Add(UpnpService service, string localIp, int leaseSeconds)
        {
            string p = port.ToString(CultureInfo.InvariantCulture);
            string[] args =
            {
                "NewRemoteHost", "",
                "NewExternalPort", p,
                "NewProtocol", "TCP",
                "NewInternalPort", p,
                "NewInternalClient", localIp,
                "NewEnabled", "1",
                "NewPortMappingDescription", description,
                "NewLeaseDuration", leaseSeconds.ToString(CultureInfo.InvariantCulture),
            };
            return Soap(service, "AddPortMapping", args, HttpTimeoutMs);
        }

        private string[] DeleteArgs()
        {
            return new[]
            {
                "NewRemoteHost", "",
                "NewExternalPort", port.ToString(CultureInfo.InvariantCulture),
                "NewProtocol", "TCP",
            };
        }

        private bool IsOurOwnMapping(UpnpService service, string localIp)
        {
            try
            {
                string[] args =
                {
                    "NewRemoteHost", "",
                    "NewExternalPort", port.ToString(CultureInfo.InvariantCulture),
                    "NewProtocol", "TCP",
                };
                HttpResponse entry = Soap(service, "GetSpecificPortMappingEntry", args, HttpTimeoutMs);
                if (entry.Status != 200)
                {
                    return false;
                }
                string client = ParseSoapValue(entry.Body, "NewInternalClient");
                string internalPort = ParseSoapValue(entry.Body, "NewInternalPort");
                return client == localIp && internalPort == port.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string DescribeFailure(string action, HttpResponse response)
        {
            int code = ParseSoapErrorCode(response.Body, out string text);
            if (code == 718)
            {
                return action + " failed: 718 ConflictInMappingEntry (port already forwarded to another device)";
            }
            if (code > 0)
            {
                return action + " failed: " + code + (string.IsNullOrEmpty(text) ? "" : " " + text);
            }
            return action + " failed: HTTP " + response.Status;
        }

        // ---- SSDP ----

        private static List<string> Discover(string lanIp, int timeoutMs)
        {
            List<string> locations = new List<string>();
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                IPAddress bindAddress = IPAddress.Any;
                IPAddress parsed;
                if (!string.IsNullOrEmpty(lanIp) && IPAddress.TryParse(lanIp, out parsed))
                {
                    bindAddress = parsed;
                }

                socket.Bind(new IPEndPoint(bindAddress, 0));
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
                    if (!bindAddress.Equals(IPAddress.Any))
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, bindAddress.GetAddressBytes());
                    }
                }
                catch (SocketException)
                {
                    // Defaults are fine where these options are unsupported.
                }

                IPEndPoint group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                for (int round = 0; round < 2; round++)
                {
                    foreach (string st in SearchTargets)
                    {
                        byte[] packet = Encoding.ASCII.GetBytes(BuildMSearch(st));
                        try
                        {
                            socket.SendTo(packet, group);
                        }
                        catch (SocketException)
                        {
                            // e.g. no multicast route; the receive loop then just times out.
                        }
                    }
                }

                Stopwatch clock = Stopwatch.StartNew();
                long deadline = timeoutMs;
                byte[] buffer = new byte[4096];
                while (true)
                {
                    long remaining = deadline - clock.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    if (!socket.Poll((int)Math.Min(remaining, 200) * 1000, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    int length;
                    try
                    {
                        length = socket.ReceiveFrom(buffer, ref from);
                    }
                    catch (SocketException)
                    {
                        // ICMP port unreachable and similar show up here on Windows; keep listening.
                        continue;
                    }

                    string location = ParseSsdpLocation(Encoding.ASCII.GetString(buffer, 0, length));
                    if (location != null && !locations.Contains(location))
                    {
                        if (locations.Count == 0)
                        {
                            // Routers answer within milliseconds; after the first answer wait only briefly for others.
                            deadline = Math.Min(deadline, clock.ElapsedMilliseconds + 1000);
                        }
                        locations.Add(location);
                    }
                }
            }
            return locations;
        }

        internal static string BuildMSearch(string searchTarget)
        {
            return "M-SEARCH * HTTP/1.1\r\n"
                + "HOST: 239.255.255.250:1900\r\n"
                + "MAN: \"ssdp:discover\"\r\n"
                + "MX: 2\r\n"
                + "ST: " + searchTarget + "\r\n"
                + "\r\n";
        }

        /// <summary>LOCATION of an SSDP "HTTP/1.1 200 OK" answer (header name case-insensitive); null otherwise.</summary>
        internal static string ParseSsdpLocation(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return null;
            }

            string[] lines = response.Replace("\r\n", "\n").Split('\n');
            string[] status = lines[0].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (status.Length < 2 || !status[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) || status[1] != "200")
            {
                return null;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                string name = lines[i].Substring(0, colon).Trim();
                if (!string.Equals(name, "LOCATION", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = lines[i].Substring(colon + 1).Trim();
                Uri uri;
                if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttp)
                {
                    return value;
                }
                return null;
            }
            return null;
        }

        // ---- device description ----

        /// <summary>
        /// WAN connection services in the description XML, WANIPConnection (any version) first, then WANPPPConnection,
        /// each in document order, with controlURL resolved against URLBase (if present) or the LOCATION url.
        /// Namespace-agnostic: elements are matched by local name.
        /// </summary>
        internal static List<UpnpService> FindWanServices(string descriptionXml, string location)
        {
            List<UpnpService> ip = new List<UpnpService>();
            List<UpnpService> ppp = new List<UpnpService>();

            XmlDocument doc = LoadXml(descriptionXml);
            if (doc == null || doc.DocumentElement == null)
            {
                return ip;
            }

            string baseUrl = location;
            foreach (XmlElement child in ChildElements(doc.DocumentElement))
            {
                if (child.LocalName == "URLBase" && child.InnerText.Trim().Length > 0)
                {
                    baseUrl = child.InnerText.Trim();
                }
            }

            foreach (XmlElement service in Descendants(doc.DocumentElement, "service"))
            {
                string type = null;
                string control = null;
                foreach (XmlElement child in ChildElements(service))
                {
                    if (child.LocalName == "serviceType")
                    {
                        type = child.InnerText.Trim();
                    }
                    else if (child.LocalName == "controlURL")
                    {
                        control = child.InnerText.Trim();
                    }
                }

                if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(control))
                {
                    continue;
                }

                bool isIp = type.StartsWith(ServiceWanIp, StringComparison.Ordinal);
                bool isPpp = type.StartsWith(ServiceWanPpp, StringComparison.Ordinal);
                if (!isIp && !isPpp)
                {
                    continue;
                }

                string url = ResolveUrl(baseUrl, control) ?? ResolveUrl(location, control);
                if (url == null)
                {
                    continue;
                }

                UpnpService found = new UpnpService(type, url);
                (isIp ? ip : ppp).Add(found);
            }

            ip.AddRange(ppp);
            return ip;
        }

        /// <summary>Absolute url of <paramref name="relative"/> against <paramref name="baseUrl"/>; null when it cannot be resolved.</summary>
        internal static string ResolveUrl(string baseUrl, string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }

            Uri absolute;
            if (Uri.TryCreate(relative, UriKind.Absolute, out absolute)
                && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            {
                return absolute.ToString();
            }

            Uri baseUri;
            if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out baseUri))
            {
                return null;
            }

            Uri combined;
            if (!Uri.TryCreate(baseUri, relative, out combined))
            {
                return null;
            }
            return combined.ToString();
        }

        // ---- SOAP ----

        internal static string BuildSoapBody(string serviceType, string action, string[] nameValuePairs)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\"?>\r\n");
            sb.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" ");
            sb.Append("s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
            sb.Append("<s:Body>");
            sb.Append("<u:").Append(action).Append(" xmlns:u=\"").Append(EscapeXml(serviceType)).Append("\">");
            for (int i = 0; i + 1 < nameValuePairs.Length; i += 2)
            {
                sb.Append('<').Append(nameValuePairs[i]).Append('>');
                sb.Append(EscapeXml(nameValuePairs[i + 1]));
                sb.Append("</").Append(nameValuePairs[i]).Append('>');
            }
            sb.Append("</u:").Append(action).Append('>');
            sb.Append("</s:Body></s:Envelope>\r\n");
            return sb.ToString();
        }

        private static HttpResponse Soap(UpnpService service, string action, string[] args, int timeoutMs)
        {
            string body = BuildSoapBody(service.ServiceType, action, args);
            string[] headers =
            {
                "Content-Type", "text/xml; charset=\"utf-8\"",
                "SOAPAction", "\"" + service.ServiceType + "#" + action + "\"",
            };
            return Http(service.ControlUrl, "POST", headers, body, timeoutMs);
        }

        /// <summary>Text of the first element named <paramref name="localName"/> (any namespace); null if missing.</summary>
        internal static string ParseSoapValue(string xml, string localName)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }

            XmlDocument doc = LoadXml(xml);
            if (doc != null && doc.DocumentElement != null)
            {
                foreach (XmlElement element in Descendants(doc.DocumentElement, localName))
                {
                    return element.InnerText.Trim();
                }
                return null;
            }

            // Not well-formed (seen on cheap routers): fall back to a plain text search.
            Match match = Regex.Match(xml, "<(?:[A-Za-z0-9_\\-]+:)?" + Regex.Escape(localName) + "(?:\\s[^>]*)?>([^<]*)<");
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
        }

        /// <summary>UPnPError errorCode of a SOAP fault (e.g. 718); -1 when there is none.</summary>
        internal static int ParseSoapErrorCode(string xml, out string errorDescription)
        {
            errorDescription = ParseSoapValue(xml, "errorDescription");
            string code = ParseSoapValue(xml, "errorCode");
            int value;
            if (code != null && int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return -1;
        }

        // ---- minimal HTTP/1.1 ----

        internal sealed class HttpResponse
        {
            public int Status;
            public string Body = "";
        }

        /// <summary>One request on a fresh connection (Connection: close). Throws on network errors and timeouts.</summary>
        private static HttpResponse Http(string url, string method, string[] headers, string body, int timeoutMs)
        {
            Uri uri = new Uri(url);
            if (uri.Scheme != Uri.UriSchemeHttp)
            {
                throw new IOException("only http:// is supported");
            }

            Stopwatch clock = Stopwatch.StartNew();
            byte[] bodyBytes = body == null ? new byte[0] : Encoding.UTF8.GetBytes(body);

            StringBuilder request = new StringBuilder();
            request.Append(method).Append(' ').Append(uri.PathAndQuery).Append(" HTTP/1.1\r\n");
            request.Append("Host: ").Append(uri.Authority).Append("\r\n");
            request.Append("Connection: close\r\n");
            request.Append("User-Agent: UPnP/1.1 MiniClient/1.0\r\n");
            if (headers != null)
            {
                for (int i = 0; i + 1 < headers.Length; i += 2)
                {
                    request.Append(headers[i]).Append(": ").Append(headers[i + 1]).Append("\r\n");
                }
            }
            if (body != null)
            {
                request.Append("Content-Length: ").Append(bodyBytes.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            }
            request.Append("\r\n");

            using (TcpClient client = new TcpClient(AddressFamily.InterNetwork))
            {
                IAsyncResult connect = client.BeginConnect(uri.Host, uri.Port, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    throw new IOException("connect timed out");
                }
                client.EndConnect(connect);
                client.NoDelay = true;

                NetworkStream stream = client.GetStream();
                stream.WriteTimeout = timeoutMs;
                byte[] head = Encoding.ASCII.GetBytes(request.ToString());
                stream.Write(head, 0, head.Length);
                if (bodyBytes.Length > 0)
                {
                    stream.Write(bodyBytes, 0, bodyBytes.Length);
                }

                MemoryStream received = new MemoryStream();
                byte[] buffer = new byte[4096];
                while (true)
                {
                    int remaining = timeoutMs - (int)clock.ElapsedMilliseconds;
                    if (remaining <= 0)
                    {
                        throw new IOException("HTTP response timed out");
                    }

                    stream.ReadTimeout = remaining;
                    int n;
                    try
                    {
                        n = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        // Timeout or reset: use what arrived if it forms a response.
                        n = 0;
                        if (clock.ElapsedMilliseconds >= timeoutMs && received.Length == 0)
                        {
                            throw;
                        }
                    }

                    bool closed = n <= 0;
                    if (n > 0)
                    {
                        received.Write(buffer, 0, n);
                    }

                    HttpResponse parsed = ParseHttpResponse(received.GetBuffer(), (int)received.Length, closed);
                    if (parsed != null)
                    {
                        return parsed;
                    }
                    if (closed)
                    {
                        throw new IOException("connection closed before a full HTTP response");
                    }
                }
            }
        }

        /// <summary>
        /// Parses a raw HTTP/1.x response (Content-Length, chunked, or read-to-close when <paramref name="closed"/>).
        /// Returns null while the response is still incomplete.
        /// </summary>
        internal static HttpResponse ParseHttpResponse(byte[] data, int length, bool closed)
        {
            int headerEnd = IndexOf(data, length, new byte[] { 13, 10, 13, 10 });
            int bodyStart = headerEnd + 4;
            if (headerEnd < 0)
            {
                headerEnd = IndexOf(data, length, new byte[] { 10, 10 });
                bodyStart = headerEnd + 2;
            }
            if (headerEnd < 0)
            {
                return null;
            }

            string headerText = Encoding.ASCII.GetString(data, 0, headerEnd);
            string[] lines = headerText.Replace("\r\n", "\n").Split('\n');
            string[] statusParts = lines[0].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int status;
            if (statusParts.Length < 2 || !int.TryParse(statusParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
            {
                throw new IOException("bad HTTP status line");
            }

            int contentLength = -1;
            bool chunked = false;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out contentLength);
                }
                else if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                    && value.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    chunked = true;
                }
            }

            byte[] bodyBytes;
            if (chunked)
            {
                bool complete;
                bodyBytes = DecodeChunked(data, bodyStart, length, out complete);
                if (!complete && !closed)
                {
                    return null;
                }
            }
            else if (contentLength >= 0)
            {
                int available = length - bodyStart;
                if (available < contentLength && !closed)
                {
                    return null;
                }
                int take = Math.Min(available, contentLength);
                bodyBytes = new byte[take];
                Buffer.BlockCopy(data, bodyStart, bodyBytes, 0, take);
            }
            else
            {
                if (!closed)
                {
                    return null;
                }
                bodyBytes = new byte[length - bodyStart];
                Buffer.BlockCopy(data, bodyStart, bodyBytes, 0, bodyBytes.Length);
            }

            HttpResponse response = new HttpResponse();
            response.Status = status;
            response.Body = Encoding.UTF8.GetString(bodyBytes);
            return response;
        }

        private static byte[] DecodeChunked(byte[] data, int start, int length, out bool complete)
        {
            complete = false;
            MemoryStream output = new MemoryStream();
            int pos = start;
            while (pos < length)
            {
                int lineEnd = IndexOf(data, length, new byte[] { 13, 10 }, pos);
                if (lineEnd < 0)
                {
                    break;
                }

                string sizeText = Encoding.ASCII.GetString(data, pos, lineEnd - pos);
                int semicolon = sizeText.IndexOf(';');
                if (semicolon >= 0)
                {
                    sizeText = sizeText.Substring(0, semicolon);
                }

                int size;
                if (!int.TryParse(sizeText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out size) || size < 0)
                {
                    break;
                }

                int chunkStart = lineEnd + 2;
                if (size == 0)
                {
                    complete = true;
                    break;
                }
                if (chunkStart + size > length)
                {
                    break;
                }

                output.Write(data, chunkStart, size);
                pos = chunkStart + size + 2; // skip the CRLF after the chunk data
            }
            return output.ToArray();
        }

        private static int IndexOf(byte[] data, int length, byte[] pattern, int from = 0)
        {
            for (int i = from; i + pattern.Length <= length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }
            }
            return -1;
        }

        // ---- helpers ----

        /// <summary>The local IPv4 the OS would use to reach the router at <paramref name="url"/>; null if unknown.</summary>
        private static string LocalIpToward(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                IPAddress router;
                if (!IPAddress.TryParse(uri.Host, out router) || router.AddressFamily != AddressFamily.InterNetwork)
                {
                    return null;
                }

                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect(new IPEndPoint(router, uri.Port));
                    IPEndPoint local = socket.LocalEndPoint as IPEndPoint;
                    if (local == null || local.Address.Equals(IPAddress.Any) || IPAddress.IsLoopback(local.Address))
                    {
                        return null;
                    }
                    return local.Address.ToString();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static XmlDocument LoadXml(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }

            // Some routers send a BOM or stray bytes before the declaration.
            int first = xml.IndexOf('<');
            if (first < 0)
            {
                return null;
            }
            string text = xml.Substring(first);

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings();
                settings.DtdProcessing = DtdProcessing.Ignore;
                settings.XmlResolver = null;
                using (StringReader sr = new StringReader(text))
                using (XmlReader reader = XmlReader.Create(sr, settings))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.XmlResolver = null;
                    doc.Load(reader);
                    return doc;
                }
            }
            catch (XmlException)
            {
                return null;
            }
        }

        private static IEnumerable<XmlElement> ChildElements(XmlElement parent)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                XmlElement element = node as XmlElement;
                if (element != null)
                {
                    yield return element;
                }
            }
        }

        private static IEnumerable<XmlElement> Descendants(XmlElement root, string localName)
        {
            foreach (XmlNode node in root.GetElementsByTagName("*"))
            {
                XmlElement element = node as XmlElement;
                if (element != null && element.LocalName == localName)
                {
                    yield return element;
                }
            }
        }

        private static string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>A WAN connection service found in a router description: its type and absolute control url.</summary>
    internal sealed class UpnpService
    {
        public readonly string ServiceType;
        public readonly string ControlUrl;

        public UpnpService(string serviceType, string controlUrl)
        {
            ServiceType = serviceType;
            ControlUrl = controlUrl;
        }
    }
}
