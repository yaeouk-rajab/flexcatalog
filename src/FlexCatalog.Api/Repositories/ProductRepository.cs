using FlexCatalog.Api.Auth;
using FlexCatalog.Api.Domain;
using FlexCatalog.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FlexCatalog.Api.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);

    Task InsertAsync(Product product, CancellationToken ct = default);

    Task<bool> ReplaceAsync(Product product, CancellationToken ct = default);

    Task<bool> DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Runs a caller-built $facet-style aggregation pipeline (expected to
    /// yield exactly one output document) with the tenant scope forcibly
    /// applied: a leading tenant $match stage, or, for a pipeline that starts
    /// with a $text match, the tenant condition inside that same $match (MongoDB
    /// requires $text in the first stage). This is the ONLY way search/facet queries reach
    /// the collection, so a caller cannot construct a pipeline that skips
    /// tenant scoping even by accident.
    /// </summary>
    Task<BsonDocument?> AggregateTenantScopedAsync(IEnumerable<BsonDocument> pipelineStages, CancellationToken ct = default);
}

public sealed class ProductRepository(MongoContext context, ITenantContext tenantContext) : IProductRepository
{
    private IMongoCollection<Product> Collection => context.Products;

    private FilterDefinition<Product> TenantFilter =>
        Builders<Product>.Filter.Eq(p => p.TenantId, tenantContext.TenantId);

    public async Task<Product?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Id, id);
        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Sku, sku);
        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task InsertAsync(Product product, CancellationToken ct = default)
    {
        // Never trust a caller-supplied tenantId on write; always stamp the
        // authenticated caller's own tenant.
        product.TenantId = tenantContext.TenantId;
        await Collection.InsertOneAsync(product, cancellationToken: ct);
    }

    public async Task<bool> ReplaceAsync(Product product, CancellationToken ct = default)
    {
        product.TenantId = tenantContext.TenantId;
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Id, product.Id);
        var result = await Collection.ReplaceOneAsync(filter, product, cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var filter = TenantFilter & Builders<Product>.Filter.Eq(p => p.Id, id);
        var result = await Collection.DeleteOneAsync(filter, ct);
        return result.DeletedCount > 0;
    }

    public async Task<BsonDocument?> AggregateTenantScopedAsync(IEnumerable<BsonDocument> pipelineStages, CancellationToken ct = default)
    {
        PipelineDefinition<Product, BsonDocument> pipeline =
            BuildTenantScopedPipeline(tenantContext.TenantId, pipelineStages).ToArray();
        using var cursor = await Collection.AggregateAsync(pipeline, cancellationToken: ct);
        var results = await cursor.ToListAsync(ct);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Puts the tenant scope in front of a caller-built pipeline. Normally that is a
    /// separate leading $match on tenantId. MongoDB only allows a $match containing
    /// $text as the FIRST stage of a pipeline, so when the caller's first stage is a
    /// $text match the tenant condition is folded into that same $match instead of
    /// being placed ahead of it: a separate leading stage made every full-text search
    /// fail with "$match with $text is only allowed as the first pipeline stage".
    /// The tenant condition is always applied, and a tenantId the caller put in that
    /// stage is dropped so it can never override the one from the tenant context.
    /// </summary>
    internal static List<BsonDocument> BuildTenantScopedPipeline(string tenantId, IEnumerable<BsonDocument> pipelineStages)
    {
        var stages = pipelineStages.ToList();

        if (stages.Count > 0
            && stages[0].TryGetValue("$match", out var firstMatch)
            && firstMatch is BsonDocument textMatch
            && textMatch.Contains("$text"))
        {
            var scoped = new BsonDocument("tenantId", tenantId);
            foreach (var element in textMatch.Elements.Where(e => e.Name != "tenantId"))
            {
                scoped.Add(element);
            }

            stages[0] = new BsonDocument("$match", scoped);
            return stages;
        }

        stages.Insert(0, new BsonDocument("$match", new BsonDocument("tenantId", tenantId)));
        return stages;
    }
}
