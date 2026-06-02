using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace AutoExporter.Agent
{
    /// <summary>
    /// Diagnostic only TLS check. When an https login fails, this opens its own handshake to the
    /// server so we can tell the operator WHY: an untrusted certificate chain (root CA not installed
    /// or self-signed), a name mismatch (connecting by IP instead of the name on the certificate),
    /// or a plain reachability problem. It accepts the certificate for the probe so it can read it,
    /// which does NOT change the strict validation the SDK login uses.
    /// </summary>
    internal static class TlsProbe
    {
        private const int ConnectTimeoutMs = 4000;

        /// <summary>
        /// Returns a human-readable classification for an https server, or null for non-https.
        /// </summary>
        public static string Classify(Uri serverUri)
        {
            if (serverUri == null) return null;
            if (!string.Equals(serverUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return null;

            var host = serverUri.Host;
            var port = serverUri.Port;
            X509Certificate2 cert = null;
            try
            {
                using (var tcp = new TcpClient())
                {
                    var ar = tcp.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
                        return $"cannot reach {host}:{port} (timed out).";
                    tcp.EndConnect(ar);

                    var captured = SslPolicyErrors.None;
                    using (var ssl = new SslStream(tcp.GetStream(), false,
                        (s, c, chain, errors) =>
                        {
                            captured = errors;
                            if (c != null) cert = new X509Certificate2(c);
                            return true;   // probe only, does not affect the SDK login
                        }))
                    {
                        ssl.AuthenticateAsClient(host);
                    }

                    if (captured == SslPolicyErrors.None)
                        return "TLS OK, the certificate is trusted.";

                    var parts = new List<string>();
                    if ((captured & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                        parts.Add("the certificate chain is not trusted on this machine (root CA not installed, or self-signed)");
                    if ((captured & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                        parts.Add($"the certificate name does not match '{host}' (you may be connecting by IP rather than the host name on the certificate)");
                    if ((captured & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
                        parts.Add("the server did not present a certificate");

                    var who = cert != null
                        ? $" Certificate subject={cert.Subject}, issuer={cert.Issuer}, thumbprint={cert.Thumbprint}."
                        : "";
                    return "Server TLS certificate is not accepted: " + string.Join(", ", parts) + "." + who;
                }
            }
            catch (Exception ex)
            {
                return $"TLS probe to {host}:{port} failed: {ex.Message}";
            }
            finally
            {
                // The certificate built in the validation callback holds an unmanaged handle.
                cert?.Dispose();
            }
        }
    }
}
