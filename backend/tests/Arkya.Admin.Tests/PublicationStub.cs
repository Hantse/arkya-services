using System.Net;
using System.Text.Json;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

internal sealed partial class ContentStub
{
    public static readonly Guid PostId = Guid.Parse("a5bba1ae-6767-4a91-b294-78ccb1385daf");
    public ContentPostResponse Post { get; set; } = new(PostId, 3, "release-note", "fr", "Une île à découvrir",
        "Les nouveautés de la bêta.", "Nouveautés\nExploration et connexion.", "2026.10.23",
        new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero), new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero), null, null);
    public bool PostsHaveMore { get; set; }
    public List<string> PostQueries { get; } = [];
    public HttpStatusCode? PostReadError { get; set; }
    public bool PostDeleted { get; private set; }

    private HttpResponseMessage HandlePublication(ContentCall call, string query)
    {
        if (call.Method == HttpMethod.Delete)
        {
            var revisionQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query)["revision"];
            if (!long.TryParse(revisionQuery, out var expected) || expected != Post.Revision)
                return new(HttpStatusCode.Conflict);
            PostDeleted = true;
            return new(HttpStatusCode.NoContent);
        }
        if (call.Method == HttpMethod.Get)
        {
            PostQueries.Add(query);
            if (PostReadError is { } error) return new(error);
            if (PostDeleted) return new(HttpStatusCode.NotFound);
            return call.Path == "/admin/content/posts" ? Json(new ContentPostList([Post], PostsHaveMore)) : Json(Post);
        }
        if (call.Path == "/admin/content/posts" && call.Method == HttpMethod.Post)
        {
            var input = call.Body!.Value.Deserialize<CreateContentPost>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Post = Post with { Revision = 1, Kind = input.Kind, Language = input.Language, Title = input.Title,
                Summary = input.Summary, Body = input.Body, GameVersion = input.GameVersion, PublishedAt = null, PublishedRevision = null };
            return Json(Post);
        }
        var revision = call.Body!.Value.GetProperty("revision").GetInt64();
        if (revision != Post.Revision) return new(HttpStatusCode.Conflict);
        if (call.Method == HttpMethod.Put)
        {
            var input = call.Body.Value.Deserialize<UpdateContentPost>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Post = Post with { Revision = revision + 1, Kind = input.Kind, Language = input.Language, Title = input.Title,
                Summary = input.Summary, Body = input.Body, GameVersion = input.GameVersion };
        }
        else if (call.Path.EndsWith("/publish", StringComparison.Ordinal))
            Post = Post with { Revision = revision + 1, PublishedRevision = revision + 1, PublishedAt = Post.UpdatedAt };
        else if (call.Path.EndsWith("/unpublish", StringComparison.Ordinal))
            Post = Post with { Revision = revision + 1, PublishedRevision = null, PublishedAt = null };
        else return new(HttpStatusCode.NotFound);
        return Json(Post);
    }
}
