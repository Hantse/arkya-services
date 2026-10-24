using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;

namespace Arkya.Content.Api.Endpoints;

public static class PostEndpoints
{
    public const string PublicCorsPolicy = "PublicContent";

    public static void MapPostEndpoints(this WebApplication app, RouteGroupBuilder admin)
    {
        admin.MapGet("/posts", async (string? kind, string? language, string? q, string? gameVersion, string? from, string? to,
            string? sort, int? limit, int? offset, PostService service, CancellationToken ct) =>
        {
            var errors = PostService.ValidateFeedQuery(kind, language, limit ?? 20, offset ?? 0, q, gameVersion, from, to, sort, out var filter);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var posts = await service.ListAsync(false, kind, language, limit ?? 20, offset ?? 0, ct, filter);
            return Results.Ok(new PostPage<AdminPostResponse>(posts.Take(limit ?? 20).Select(AdminPostResponse.From).ToArray(), posts.Count > (limit ?? 20)));
        });
        admin.MapGet("/posts/{id:guid}", async (Guid id, PostService service, CancellationToken ct) =>
            await service.FindAsync(id, ct) is { } post ? Results.Ok(AdminPostResponse.From(post)) : Results.NotFound());
        admin.MapPost("/posts", async (CreatePostRequest request, PostService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, ct);
            return result.Status == PostChangeStatus.Saved
                ? Results.Created($"/admin/content/posts/{result.Post!.Id:D}", AdminPostResponse.From(result.Post)) : Response(result);
        });
        admin.MapPut("/posts/{id:guid}", async (Guid id, UpdatePostRequest request, PostService service, CancellationToken ct) =>
            Response(await service.UpdateAsync(id, request, ct)));
        admin.MapPost("/posts/{id:guid}/publish", async (Guid id, PostRevisionRequest request, PostService service, CancellationToken ct) =>
            Response(await service.PublishAsync(id, request.Revision, true, ct)));
        admin.MapPost("/posts/{id:guid}/unpublish", async (Guid id, PostRevisionRequest request, PostService service, CancellationToken ct) =>
            Response(await service.PublishAsync(id, request.Revision, false, ct)));
        admin.MapDelete("/posts/{id:guid}", async (Guid id, long? revision, PostService service, CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(id, revision, ct);
            return result.Status == PostChangeStatus.Saved ? Results.NoContent() : Response(result);
        });

        var feed = app.MapGroup("/v1/content/posts").RequireCors(PublicCorsPolicy);
        feed.MapGet("", async (string? kind, string? language, string? q, string? gameVersion, string? from, string? to,
            string? sort, int? limit, int? offset, PostService service, CancellationToken ct) =>
        {
            var errors = PostService.ValidateFeedQuery(kind, language, limit ?? 20, offset ?? 0, q, gameVersion, from, to, sort, out var filter);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var posts = await service.ListAsync(true, kind, language, limit ?? 20, offset ?? 0, ct, filter);
            return Results.Ok(new PostPage<PublicPostResponse>(posts.Take(limit ?? 20).Select(PublicPostResponse.From).ToArray(), posts.Count > (limit ?? 20)));
        });
        feed.MapGet("/{id:guid}", async (Guid id, PostService service, CancellationToken ct) =>
            await service.FindAsync(id, ct) is { Published: not null } post ? Results.Ok(PublicPostResponse.From(post)) : Results.NotFound());
    }

    private static IResult Response(PostChangeResult result) => result.Status switch
    {
        PostChangeStatus.Saved => Results.Ok(AdminPostResponse.From(result.Post!)),
        PostChangeStatus.Invalid => Results.ValidationProblem(result.Errors!),
        PostChangeStatus.NotFound => Results.NotFound(),
        _ => Results.Problem(statusCode: 409, title: "The publication has changed. Reload before trying again.")
    };
}
