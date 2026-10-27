using FlexCatalog.Api.Repositories;
using MongoDB.Bson;

namespace FlexCatalog.UnitTests.Repositories;

/// <summary>
/// ProductRepository.BuildTenantScopedPipeline decides where the tenant filter goes. The
/// $text case matters because MongoDB only allows a $match containing $text as the first
/// pipeline stage, and the compound text index (tenantId + text fields) needs an equality
/// condition on tenantId in that same stage.
/// </summary>
public class ProductRepositoryTenantPipelineTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public void WithoutText_PrependsATenantMatchStage()
    {
        var facet = new BsonDocument("$facet", new BsonDocument("results", new BsonArray()));

        var pipeline = ProductRepository.BuildTenantScopedPipeline(TenantId, [facet]);

        Assert.Equal(2, pipeline.Count);
        Assert.Equal(TenantId, pipeline[0]["$match"]["tenantId"].AsString);
        Assert.Equal(1, pipeline[0]["$match"].AsBsonDocument.ElementCount);
        Assert.Equal(facet, pipeline[1]);
    }

    [Fact]
    public void WithTextMatchFirst_FoldsTheTenantIntoTheSameMatch()
    {
        var textMatch = new BsonDocument("$match", new BsonDocument("$text", new BsonDocument("$search", "wireless")));
        var addFields = new BsonDocument("$addFields", new BsonDocument("textScore", new BsonDocument("$meta", "textScore")));
        var facet = new BsonDocument("$facet", new BsonDocument("results", new BsonArray()));

        var pipeline = ProductRepository.BuildTenantScopedPipeline(TenantId, [textMatch, addFields, facet]);

        // No separate leading tenant stage: $text must be in the first stage.
        Assert.Equal(3, pipeline.Count);
        var first = pipeline[0]["$match"].AsBsonDocument;
        Assert.Equal(TenantId, first["tenantId"].AsString);
        Assert.Equal("wireless", first["$text"]["$search"].AsString);
        Assert.Equal(addFields, pipeline[1]);
        Assert.Equal(facet, pipeline[2]);
    }

    [Fact]
    public void WithTextMatch_IgnoresATenantIdSuppliedByTheCaller()
    {
        var textMatch = new BsonDocument("$match", new BsonDocument
        {
            ["tenantId"] = "someone-else",
            ["$text"] = new BsonDocument("$search", "wireless"),
        });

        var pipeline = ProductRepository.BuildTenantScopedPipeline(TenantId, [textMatch]);

        var first = pipeline[0]["$match"].AsBsonDocument;
        Assert.Equal(TenantId, first["tenantId"].AsString);
        Assert.Equal(1, first.Names.Count(n => n == "tenantId"));
    }

    [Fact]
    public void TextMatchThatIsNotFirst_StillGetsALeadingTenantMatch()
    {
        var facet = new BsonDocument("$facet", new BsonDocument("results", new BsonArray()));
        var textMatch = new BsonDocument("$match", new BsonDocument("$text", new BsonDocument("$search", "x")));

        var pipeline = ProductRepository.BuildTenantScopedPipeline(TenantId, [facet, textMatch]);

        Assert.Equal(3, pipeline.Count);
        Assert.Equal(TenantId, pipeline[0]["$match"]["tenantId"].AsString);
    }
}
