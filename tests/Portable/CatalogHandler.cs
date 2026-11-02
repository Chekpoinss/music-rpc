using System.Net;

sealed class CatalogHandler : HttpMessageHandler
{
    public int Requests { get; private set; }
    public bool Offline { get; set; } = true;
    public bool Empty { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        if (request.RequestUri!.Host == "itunes.apple.com")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        if (Offline) throw new HttpRequestException("offline fixture");
        var json = Empty ? "{\"data\":[]}" : """
            {"data":[{"title":"Not Allowed","duration":167,"link":"https://www.deezer.com/track/1","artist":{"name":"TV Girl"},"album":{"title":"Who Really Cares","cover_big":"https://cdn-images.dzcdn.net/images/cover/1/500.jpg"}}]}
            """;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
