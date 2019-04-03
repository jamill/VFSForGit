using GVFS.Common;
using GVFS.Tests.Should;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class OrgInfoServerTests
    {
        private string response_1 = @"{""Version"":""1.2.3.4""}";
        private string baseUrl = "https://www.contoso.com";

        private interface IHttpMessageHandlerProtectedMembers
        {
            Task<HttpResponseMessage>  SendAsync(HttpRequestMessage message, CancellationToken token);
        }

        [TestCase]
        public void QueryNewestVersion()
        {
            Mock<HttpMessageHandler> handlerMock = new Mock<HttpMessageHandler>();

            handlerMock.Protected().As<IHttpMessageHandlerProtectedMembers>()
                .Setup(m => m.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpResponseMessage()
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(this.response_1)
                    })
                .Verifiable();

            HttpClient httpClient = new HttpClient(handlerMock.Object);

            OrgInfoServer upgradeChecker = new OrgInfoServer(httpClient, this.baseUrl);
            Version version = upgradeChecker.QueryNewestVersion();

            version.ShouldEqual(new Version("1.2.3.4"));
        }

        [TestCase]
        public void HandleTimeout()
        {
        }

        [TestCase]
        public void Handle401()
        {
        }
    }
}
