(function () {
    var shortUrl = prompt("Desired short url, if any.");
    var target = 'http://go.clcohio.org/Link/CreateLink?link.URL=' + encodeURIComponent(location.href);
    if (shortUrl) {
        target += '&link.shortURL=' + encodeURIComponent(shortUrl);
    }
    window.open(target, '_blank', 'width=500,height=200');
})();
