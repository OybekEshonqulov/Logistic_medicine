using FluentValidation;
using Microsoft.EntityFrameworkCore;
using PharmaTrack.Application.Common;
using PharmaTrack.Domain.Entities;

namespace PharmaTrack.Application.Features.References;

public sealed record SupplierDto(Guid Id, string Name, string? Phone, bool IsActive);
public sealed record SaveSupplierRequest(string Name, string? Phone, bool IsActive = true);
public sealed record ManufacturerDto(Guid Id, string Name, string? Country, bool IsActive);
public sealed record SaveManufacturerRequest(string Name, string? Country, bool IsActive = true);
public sealed record ProductDto(Guid Id, string Name, string Form, string? Dosage, Guid ManufacturerId, string ManufacturerName, string? Gtin, bool IsActive);
public sealed record SaveProductRequest(string Name, string Form, string? Dosage, Guid ManufacturerId, string? Gtin, bool IsActive = true);
public sealed record ProductQuery : PageQuery
{
    public Guid? ManufacturerId { get; init; }
}

public sealed class SaveSupplierRequestValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class SaveManufacturerRequestValidator : AbstractValidator<SaveManufacturerRequest>
{
    public SaveManufacturerRequestValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Form).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Dosage).MaximumLength(100);
        RuleFor(x => x.ManufacturerId).NotEmpty();
        RuleFor(x => x.Gtin).Matches(@"^\d{8,14}$").When(x => x.Gtin is not null);
    }
}

public sealed class SupplierService(IAppDbContext db)
{
    public Task<PagedResult<SupplierDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var suppliers = db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
            suppliers = suppliers.Where(s => s.Name.ToLower().Contains(query.Search.ToLower()));
        return suppliers.OrderBy(s => s.Name).Select(s => new SupplierDto(s.Id, s.Name, s.Phone, s.IsActive)).ToPagedAsync(query, ct);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken ct) =>
        await db.Suppliers.AsNoTracking().Where(s => s.Id == id).Select(s => new SupplierDto(s.Id, s.Name, s.Phone, s.IsActive)).FirstOrNotFoundAsync(ct);

    public async Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct)
    {
        var supplier = new Supplier { Name = request.Name, Phone = request.Phone, IsActive = request.IsActive };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(ct);
        return new SupplierDto(supplier.Id, supplier.Name, supplier.Phone, supplier.IsActive);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SaveSupplierRequest request, CancellationToken ct)
    {
        var supplier = await db.Suppliers.Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
        supplier.Name = request.Name;
        supplier.Phone = request.Phone;
        supplier.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return new SupplierDto(supplier.Id, supplier.Name, supplier.Phone, supplier.IsActive);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var supplier = await db.Suppliers.Where(s => s.Id == id).FirstOrNotFoundAsync(ct);
        supplier.IsActive = false;
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ManufacturerService(IAppDbContext db)
{
    public Task<PagedResult<ManufacturerDto>> ListAsync(PageQuery query, CancellationToken ct)
    {
        var manufacturers = db.Manufacturers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
            manufacturers = manufacturers.Where(m => m.Name.ToLower().Contains(query.Search.ToLower()));
        return manufacturers.OrderBy(m => m.Name).Select(m => new ManufacturerDto(m.Id, m.Name, m.Country, m.IsActive)).ToPagedAsync(query, ct);
    }

    public async Task<ManufacturerDto> GetAsync(Guid id, CancellationToken ct) =>
        await db.Manufacturers.AsNoTracking().Where(m => m.Id == id).Select(m => new ManufacturerDto(m.Id, m.Name, m.Country, m.IsActive)).FirstOrNotFoundAsync(ct);

    public async Task<ManufacturerDto> CreateAsync(SaveManufacturerRequest request, CancellationToken ct)
    {
        var manufacturer = new Manufacturer { Name = request.Name, Country = request.Country, IsActive = request.IsActive };
        db.Manufacturers.Add(manufacturer);
        await db.SaveChangesAsync(ct);
        return new ManufacturerDto(manufacturer.Id, manufacturer.Name, manufacturer.Country, manufacturer.IsActive);
    }

    public async Task<ManufacturerDto> UpdateAsync(Guid id, SaveManufacturerRequest request, CancellationToken ct)
    {
        var manufacturer = await db.Manufacturers.Where(m => m.Id == id).FirstOrNotFoundAsync(ct);
        manufacturer.Name = request.Name;
        manufacturer.Country = request.Country;
        manufacturer.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return new ManufacturerDto(manufacturer.Id, manufacturer.Name, manufacturer.Country, manufacturer.IsActive);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var manufacturer = await db.Manufacturers.Where(m => m.Id == id).FirstOrNotFoundAsync(ct);
        manufacturer.IsActive = false;
        await db.SaveChangesAsync(ct);
    }
}

public sealed class ProductService(IAppDbContext db)
{
    public Task<PagedResult<ProductDto>> ListAsync(ProductQuery query, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();
        if (query.ManufacturerId is { } manufacturerId) products = products.Where(p => p.ManufacturerId == manufacturerId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            products = products.Where(p => p.Name.ToLower().Contains(search) || p.Gtin == query.Search);
        }

        return Project(products.OrderBy(p => p.Name)).ToPagedAsync(query, ct);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Products.AsNoTracking().Where(p => p.Id == id)).FirstOrNotFoundAsync(ct);

    public async Task<ProductDto> CreateAsync(SaveProductRequest request, CancellationToken ct)
    {
        await db.Manufacturers.Where(m => m.Id == request.ManufacturerId).FirstOrNotFoundAsync(ct);
        var product = new Product { Name = request.Name, Form = request.Form, Dosage = request.Dosage, ManufacturerId = request.ManufacturerId, Gtin = request.Gtin, IsActive = request.IsActive };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.Where(p => p.Id == id).FirstOrNotFoundAsync(ct);
        await db.Manufacturers.Where(m => m.Id == request.ManufacturerId).FirstOrNotFoundAsync(ct);

        product.Name = request.Name;
        product.Form = request.Form;
        product.Dosage = request.Dosage;
        product.ManufacturerId = request.ManufacturerId;
        product.Gtin = request.Gtin;
        product.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.Where(p => p.Id == id).FirstOrNotFoundAsync(ct);
        product.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    private static IQueryable<ProductDto> Project(IQueryable<Product> products) => products.Select(p => new ProductDto(
        p.Id, p.Name, p.Form, p.Dosage, p.ManufacturerId, p.Manufacturer.Name, p.Gtin, p.IsActive));
}
