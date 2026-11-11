using System.Text;
using OnlineSeverGame.Server;

namespace OnlineSeverGame.Server.Tests;

/// <summary>UPnP IGD port mapper (Shared/Server/UpnpPortMapper.cs): the pure parsing helpers, no network.</summary>
public class UpnpTests
{
    private const string Location = "http://192.168.0.1:1900/igd.xml";

    private const string WanIp1Description = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <specVersion><major>1</major><minor>0</minor></specVersion>
          <device>
            <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:Layer3Forwarding:1</serviceType>
                <controlURL>/ctl/L3F</controlURL>
              </service>
            </serviceList>
            <deviceList>
              <device>
                <deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType>
                <serviceList>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1</serviceType>
                    <controlURL>/ctl/CmnIfCfg</controlURL>
                  </service>
                </serviceList>
                <deviceList>
                  <device>
                    <deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType>
                    <serviceList>
                      <service>
                        <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                        <serviceId>urn:upnp-org:serviceId:WANIPConn1</serviceId>
                        <controlURL>/ctl/IPConn</controlURL>
                        <eventSubURL>/evt/IPConn</eventSubURL>
                        <SCPDURL>/WANIPCn.xml</SCPDURL>
                      </service>
                    </serviceList>
                  </device>
                </deviceList>
              </device>
            </deviceList>
          </device>
        </root>
        """;

    private static string Description(string serviceType, string controlUrl, string urlBase = null)
    {
        string baseElement = urlBase == null ? "" : "<URLBase>" + urlBase + "</URLBase>";
        return "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\">" + baseElement
            + "<device><deviceList><device><deviceList><device><serviceList><service>"
            + "<serviceType>" + serviceType + "</serviceType><controlURL>" + controlUrl + "</controlURL>"
            + "</service></serviceList></device></deviceList></device></deviceList></device></root>";
    }

    [Fact]
    public void FindsWanIpConnection1NestedInWanConnectionDevice()
    {
        var services = UpnpPortMapper.FindWanServices(WanIp1Description, Location);

        Assert.Single(services);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:1", services[0].ServiceType);
        Assert.Equal("http://192.168.0.1:1900/ctl/IPConn", services[0].ControlUrl);
    }

    [Fact]
    public void FindsWanPppConnection()
    {
        string xml = Description("urn:schemas-upnp-org:service:WANPPPConnection:1", "/upnp/control/WANPPPConn1");

        var services = UpnpPortMapper.FindWanServices(xml, Location);

        Assert.Single(services);
        Assert.Equal("urn:schemas-upnp-org:service:WANPPPConnection:1", services[0].ServiceType);
        Assert.Equal("http://192.168.0.1:1900/upnp/control/WANPPPConn1", services[0].ControlUrl);
    }

    [Fact]
    public void FindsWanIpConnection2()
    {
        string xml = Description("urn:schemas-upnp-org:service:WANIPConnection:2", "/ctl/IPConn2");

        var services = UpnpPortMapper.FindWanServices(xml, Location);

        Assert.Single(services);
        Assert.Equal("urn:schemas-upnp-org:service:WANIPConnection:2", services[0].ServiceType);
    }

    [Fact]
    public void PrefersWanIpOverWanPppEvenWhenPppComesFirst()
    {
        string xml = "<root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><serviceList>"
            + "<service><serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType><controlURL>/ppp</controlURL></service>"
            + "<service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>/ip</controlURL></service>"
            + "</serviceList></device></root>";

        var services = UpnpPortMapper.FindWanServices(xml, Location);

        Assert.Equal(2, services.Count);
        Assert.Equal("http://192.168.0.1:1900/ip", services[0].ControlUrl);
        Assert.Equal("http://192.168.0.1:1900/ppp", services[1].ControlUrl);
    }

    [Fact]
    public void IgnoresDescriptionWithoutWanConnection()
    {
        string xml = Description("urn:schemas-upnp-org:service:Layer3Forwarding:1", "/l3f");

        Assert.Empty(UpnpPortMapper.FindWanServices(xml, Location));
        Assert.Empty(UpnpPortMapper.FindWanServices("not xml", Location));
    }

    [Fact]
    public void WorksWithoutXmlNamespaceAndWithBomAndPrefix()
    {
        string xml = "﻿<?xml version=\"1.0\"?><d:root xmlns:d=\"urn:schemas-upnp-org:device-1-0\"><d:device><d:serviceList><d:service>"
            + "<d:serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</d:serviceType><d:controlURL>/c</d:controlURL>"
            + "</d:service></d:serviceList></d:device></d:root>";

        var services = UpnpPortMapper.FindWanServices(xml, Location);

        Assert.Single(services);
        Assert.Equal("http://192.168.0.1:1900/c", services[0].ControlUrl);
    }

    [Fact]
    public void ResolvesRelativeControlUrlWithoutLeadingSlashAgainstLocation()
    {
        string xml = Description("urn:schemas-upnp-org:service:WANIPConnection:1", "upnp/control/WANIPConn1");

        var services = UpnpPortMapper.FindWanServices(xml, "http://192.168.1.254:5000/rootDesc.xml");

        Assert.Equal("http://192.168.1.254:5000/upnp/control/WANIPConn1", services[0].ControlUrl);
    }

    [Fact]
    public void ResolvesControlUrlAgainstUrlBaseWhenPresent()
    {
        string xml = Description("urn:schemas-upnp-org:service:WANIPConnection:1", "/ctl/IPConn", "http://192.168.0.1:49152/");

        var services = UpnpPortMapper.FindWanServices(xml, Location);

        Assert.Equal("http://192.168.0.1:49152/ctl/IPConn", services[0].ControlUrl);
    }

    [Fact]
    public void KeepsAbsoluteControlUrl()
    {
        string xml = Description("urn:schemas-upnp-org:service:WANIPConnection:1", "http://10.0.0.1:2555/upnp/ipc", "http://192.168.0.1:49152/");

        var services = UpnpPortMapper.FindWanServices(xml, Location);

        Assert.Equal("http://10.0.0.1:2555/upnp/ipc", services[0].ControlUrl);
    }

    [Fact]
    public void ResolveUrlCases()
    {
        Assert.Equal("http://192.168.0.1:8080/a/b", UpnpPortMapper.ResolveUrl("http://192.168.0.1:8080/x.xml", "/a/b"));
        Assert.Equal("http://192.168.0.1/dir/a", UpnpPortMapper.ResolveUrl("http://192.168.0.1/dir/x.xml", "a"));
        Assert.Equal("http://192.168.0.1:5000/a", UpnpPortMapper.ResolveUrl("http://192.168.0.1:5000", "a"));
        Assert.Null(UpnpPortMapper.ResolveUrl(null, "/a"));
        Assert.Null(UpnpPortMapper.ResolveUrl("http://192.168.0.1/", ""));
    }

    [Fact]
    public void ParsesSsdpLocationCaseInsensitive()
    {
        string upper = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n"
            + "USN: uuid:abc::urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nEXT:\r\nSERVER: Linux UPnP/1.1 MiniUPnPd/2.3\r\n"
            + "LOCATION: http://192.168.0.1:1900/igd.xml\r\n\r\n";
        string lower = "HTTP/1.1 200 OK\r\nst: urn:schemas-upnp-org:service:WANIPConnection:1\r\nlocation:http://192.168.0.1:5431/dyndev/uuid:1\r\n\r\n";
        string mixed = "HTTP/1.1 200 OK\r\nLocation:  http://10.0.0.138:49152/gatedesc.xml  \r\n\r\n";

        Assert.Equal("http://192.168.0.1:1900/igd.xml", UpnpPortMapper.ParseSsdpLocation(upper));
        Assert.Equal("http://192.168.0.1:5431/dyndev/uuid:1", UpnpPortMapper.ParseSsdpLocation(lower));
        Assert.Equal("http://10.0.0.138:49152/gatedesc.xml", UpnpPortMapper.ParseSsdpLocation(mixed));
    }

    [Fact]
    public void IgnoresSsdpRequestsAndAnswersWithoutLocation()
    {
        Assert.Null(UpnpPortMapper.ParseSsdpLocation(UpnpPortMapper.BuildMSearch("ssdp:all")));
        Assert.Null(UpnpPortMapper.ParseSsdpLocation("NOTIFY * HTTP/1.1\r\nLOCATION: http://192.168.0.1/x.xml\r\n\r\n"));
        Assert.Null(UpnpPortMapper.ParseSsdpLocation("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n"));
        Assert.Null(UpnpPortMapper.ParseSsdpLocation(""));
    }

    [Fact]
    public void ParsesGetExternalIpAddressResponse()
    {
        string response = "<?xml version=\"1.0\"?>\r\n"
            + "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
            + "<s:Body><u:GetExternalIPAddressResponse xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">"
            + "<NewExternalIPAddress>203.0.113.7</NewExternalIPAddress>"
            + "</u:GetExternalIPAddressResponse></s:Body></s:Envelope>";

        Assert.Equal("203.0.113.7", UpnpPortMapper.ParseSoapValue(response, "NewExternalIPAddress"));
        Assert.Equal(-1, UpnpPortMapper.ParseSoapErrorCode(response, out _));
    }

    [Fact]
    public void ParsesSoapFaultErrorCode()
    {
        string fault = "<?xml version=\"1.0\"?>"
            + "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
            + "<s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail>"
            + "<UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\"><errorCode>718</errorCode>"
            + "<errorDescription>ConflictInMappingEntry</errorDescription></UPnPError>"
            + "</detail></s:Fault></s:Body></s:Envelope>";

        Assert.Equal(718, UpnpPortMapper.ParseSoapErrorCode(fault, out string text));
        Assert.Equal("ConflictInMappingEntry", text);
    }

    [Fact]
    public void ParsesSoapFaultFromMalformedXml()
    {
        string broken = "<s:Envelope><s:Body><s:Fault><detail><UPnPError><errorCode> 725 </errorCode>"
            + "<errorDescription>OnlyPermanentLeasesSupported</errorDescription></UPnPError></detail></s:Fault>";

        Assert.Equal(725, UpnpPortMapper.ParseSoapErrorCode(broken, out string text));
        Assert.Equal("OnlyPermanentLeasesSupported", text);
    }

    [Fact]
    public void BuildsEscapedSoapBody()
    {
        string body = UpnpPortMapper.BuildSoapBody("urn:schemas-upnp-org:service:WANIPConnection:1", "AddPortMapping",
            new[] { "NewRemoteHost", "", "NewPortMappingDescription", "Jelly <&> \"Room\"" });

        Assert.Contains("<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">", body);
        Assert.Contains("<NewRemoteHost></NewRemoteHost>", body);
        Assert.Equal("Jelly <&> \"Room\"", UpnpPortMapper.ParseSoapValue(body, "NewPortMappingDescription"));
    }

    [Fact]
    public void ParsesHttpResponseWithContentLength()
    {
        byte[] raw = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/xml\r\nContent-Length: 5\r\n\r\nhello");

        Assert.Null(UpnpPortMapper.ParseHttpResponse(raw, raw.Length - 2, closed: false));
        var response = UpnpPortMapper.ParseHttpResponse(raw, raw.Length, closed: false);
        Assert.Equal(200, response.Status);
        Assert.Equal("hello", response.Body);
    }

    [Fact]
    public void ParsesChunkedHttpResponse()
    {
        byte[] raw = Encoding.ASCII.GetBytes("HTTP/1.1 500 Internal Server Error\r\ntransfer-encoding: chunked\r\n\r\n4\r\n<a>x\r\n5;ext=1\r\n</a>!\r\n0\r\n\r\n");

        Assert.Null(UpnpPortMapper.ParseHttpResponse(raw, raw.Length - 5, closed: false));
        var response = UpnpPortMapper.ParseHttpResponse(raw, raw.Length, closed: false);
        Assert.Equal(500, response.Status);
        Assert.Equal("<a>x</a>!", response.Body);
    }

    [Fact]
    public void ParsesHttpResponseReadToClose()
    {
        byte[] raw = Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nServer: x\r\n\r\n<root/>");

        Assert.Null(UpnpPortMapper.ParseHttpResponse(raw, raw.Length, closed: false));
        Assert.Equal("<root/>", UpnpPortMapper.ParseHttpResponse(raw, raw.Length, closed: true).Body);
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("192.168.0.12", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("100.128.0.1", false)]
    [InlineData("169.254.10.10", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("203.0.113.7", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not an ip", false)]
    [InlineData("::1", false)]
    public void IsPrivateCases(string ip, bool expected)
    {
        Assert.Equal(expected, LocalAddress.IsPrivate(ip));
    }
}
