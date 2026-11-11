using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OnlineSeverGame.Server
{
    /// <summary>LAN address helpers for hosting a room on a home PC (UPnP port mapping, showing the address to share).</summary>
    public static class LocalAddress
    {
        /// <summary>
        /// The IPv4 of the interface that routes to the internet, e.g. "192.168.0.12"; null if there is none.
        /// Connecting a UDP socket sends nothing; it only asks the OS which local address it would use.
        /// </summary>
        public static string GetLanIPv4()
        {
            try
            {
                using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
                    IPEndPoint local = socket.LocalEndPoint as IPEndPoint;
                    if (local != null && IsUsable(local.Address))
                    {
                        return local.Address.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // No route to the internet (offline, no default gateway): fall back to the interface list.
            }

            return FirstInterfaceIPv4();
        }

        /// <summary>
        /// True for addresses that are not reachable from the internet: 10/8, 172.16/12, 192.168/16,
        /// 100.64/10 (carrier-grade NAT), 169.254/16 (link-local) and 127/8. False for anything that is not an IPv4.
        /// </summary>
        public static bool IsPrivate(string ipv4)
        {
            if (string.IsNullOrEmpty(ipv4))
            {
                return false;
            }

            IPAddress address;
            if (!IPAddress.TryParse(ipv4.Trim(), out address) || address.AddressFamily != AddressFamily.InterNetwork)
            {
                return false;
            }

            byte[] b = address.GetAddressBytes();
            if (b[0] == 10 || b[0] == 127)
            {
                return true;
            }
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            {
                return true;
            }
            if (b[0] == 192 && b[1] == 168)
            {
                return true;
            }
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
            {
                return true;
            }
            if (b[0] == 169 && b[1] == 254)
            {
                return true;
            }
            return false;
        }

        private static bool IsUsable(IPAddress address)
        {
            return address.AddressFamily == AddressFamily.InterNetwork
                && !IPAddress.IsLoopback(address)
                && !address.Equals(IPAddress.Any);
        }

        private static string FirstInterfaceIPv4()
        {
            string linkLocal = null;
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                        || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        IPAddress address = info.Address;
                        if (!IsUsable(address))
                        {
                            continue;
                        }

                        byte[] b = address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254)
                        {
                            // Self-assigned (no DHCP): only use it if nothing better exists.
                            if (linkLocal == null)
                            {
                                linkLocal = address.ToString();
                            }
                            continue;
                        }

                        return address.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // Some platforms do not support the interface list; report "none".
            }

            return linkLocal;
        }
    }
}
