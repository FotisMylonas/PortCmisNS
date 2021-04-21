# README

This is a fork of PortCMIS from <https://chemistry.apache.org/dotnet/portcmis.html>, using the new .net project format, supporting .net stadnard 2.0 and above.  
The driving reason behind this implementation, was the need to support atompub binding over https with invalid ssl certificates, for development reasons.
Also, i wanted to be able to define parameters such as the prefered Tls version for the ssl handshake.

PortCMIS requests are based on HttpClient. Supporting invalid certificates with HttpClient requires a code like the following one:

```csharp
using (var handler = new HttpClientHandler())
{
    handler.ServerCertificateValidationCallback = ...

    using (var client = new HttpClient(handler))
    {
        ...
    }
}
```

But the current codebase of PortCMIS at the above location, is a .netstandard 1.0 library, and thus missing support for ServerCertificateValidationCallback property.  
According to the following Microsoft doc, the property ServerCertificateValidationCallback is supported from version .NetStadnard 1.3 and above (see the Applies to table):

<https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.servercertificatecustomvalidationcallback?view=net-5.0>

So i just took the time to wrap it up in a .NET Standard 2.0 library and also wrote a new invoker class that helps with invalid ssl certificates and the ssl / tls version:

```csharp
public class HttpsAlwaysTrustCertificateInvoker : IHttpInvoker
        };
```

The class implements the "always trust" part and also gives you the ability to define the prefered ssl protocol the internal HttpClientHandler should use:

```csharp
    HttpClientHandler httpClientHandler = new HttpClientHandler();
    httpClientHandler.ClientCertificateOptions = clientOption;
    httpClientHandler.SslProtocols = prot;
    httpClientHandler.ServerCertificateCustomValidationCallback =
        (httpRequestMessage, cert, cetChain, policyErrors) =>
        {
            return true;
        };
```

You can configure PortCMIS to use the new invoker by defining the appropriate session params, like this:

```csharp
        public static IDictionary<string, string> NewSessionParameters(
            string url,
            string username,
            string password,
            string repositoryId,
            bool alwaysTrustSslCertificate,
            string sslProtocols,
            string clientCertificateOption)
        {
            Dictionary<string, string> res = new Dictionary<string, string>()
            {
            {SessionParameter.BindingType , BindingType.AtomPub},
            {SessionParameter.AtomPubUrl , url},
            {SessionParameter.RepositoryId , repositoryId},
            {SessionParameter.User , username},
            {SessionParameter.Password , password}
            };
            if (alwaysTrustSslCertificate)
            {
                res.Add(SessionParameter.HttpInvokerClass, "PortCMIS.Binding.Http.HttpsAlwaysTrustCertificateInvoker");
                res.Add(SessionParameter.SslProtocols, sslProtocols);
                res.Add(SessionParameter.ClientCertificateOption, clientCertificateOption);
            }
            return res;
        }
```
