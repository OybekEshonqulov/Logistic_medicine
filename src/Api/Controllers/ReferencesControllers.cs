using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmaTrack.Application.Common;
using PharmaTrack.Application.Features.References;

namespace PharmaTrack.Api.Controllers;

public sealed class RegionsController(RegionService regions) : ApiController
{
    [HttpGet]
    public Task<PagedResult<RegionDto>> List([FromQuery] RegionQuery query, CancellationToken ct) => regions.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<RegionDto> Get(Guid id, CancellationToken ct) => regions.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = Roles.CentralStaff)]
    public async Task<ActionResult<RegionDto>> Create(SaveRegionRequest request, CancellationToken ct)
    {
        var region = await regions.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = region.Id }, region);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public Task<RegionDto> Update(Guid id, SaveRegionRequest request, CancellationToken ct) => regions.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await regions.DeactivateAsync(id, ct);
        return NoContent();
    }
}

public sealed class DealersController(DealerService dealers) : ApiController
{
    [HttpGet]
    public Task<PagedResult<DealerDto>> List([FromQuery] DealerQuery query, CancellationToken ct) => dealers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<DealerDto> Get(Guid id, CancellationToken ct) => dealers.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<DealerDto>> Create(SaveDealerRequest request, CancellationToken ct)
    {
        var dealer = await dealers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = dealer.Id }, dealer);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.Admin)]
    public Task<DealerDto> Update(Guid id, SaveDealerRequest request, CancellationToken ct) => dealers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await dealers.DeactivateAsync(id, ct);
        return NoContent();
    }
}

public sealed class PharmaciesController(PharmacyService pharmacies) : ApiController
{
    [HttpGet]
    public Task<PagedResult<PharmacyDto>> List([FromQuery] PharmacyQuery query, CancellationToken ct) => pharmacies.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<PharmacyDto> Get(Guid id, CancellationToken ct) => pharmacies.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = $"{Roles.CentralStaff},{Roles.Dealer}")]
    public async Task<ActionResult<PharmacyDto>> Create(SavePharmacyRequest request, CancellationToken ct)
    {
        var pharmacy = await pharmacies.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = pharmacy.Id }, pharmacy);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public Task<PharmacyDto> Update(Guid id, SavePharmacyRequest request, CancellationToken ct) => pharmacies.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/approve"), Authorize(Roles = Roles.CentralStaff)]
    public Task<PharmacyDto> Approve(Guid id, CancellationToken ct) => pharmacies.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/reject"), Authorize(Roles = Roles.CentralStaff)]
    public Task<PharmacyDto> Reject(Guid id, CancellationToken ct) => pharmacies.RejectAsync(id, ct);

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await pharmacies.DeactivateAsync(id, ct);
        return NoContent();
    }
}

public sealed class ProductsController(ProductService products) : ApiController
{
    [HttpGet]
    public Task<PagedResult<ProductDto>> List([FromQuery] ProductQuery query, CancellationToken ct) => products.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ProductDto> Get(Guid id, CancellationToken ct) => products.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = Roles.CentralStaff)]
    public async Task<ActionResult<ProductDto>> Create(SaveProductRequest request, CancellationToken ct)
    {
        var product = await products.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public Task<ProductDto> Update(Guid id, SaveProductRequest request, CancellationToken ct) => products.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await products.DeactivateAsync(id, ct);
        return NoContent();
    }
}

[Authorize(Roles = Roles.CentralAll)]
public sealed class SuppliersController(SupplierService suppliers) : ApiController
{
    [HttpGet]
    public Task<PagedResult<SupplierDto>> List([FromQuery] PageQuery query, CancellationToken ct) => suppliers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<SupplierDto> Get(Guid id, CancellationToken ct) => suppliers.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = Roles.CentralStaff)]
    public async Task<ActionResult<SupplierDto>> Create(SaveSupplierRequest request, CancellationToken ct)
    {
        var supplier = await suppliers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = supplier.Id }, supplier);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public Task<SupplierDto> Update(Guid id, SaveSupplierRequest request, CancellationToken ct) => suppliers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await suppliers.DeactivateAsync(id, ct);
        return NoContent();
    }
}

public sealed class ManufacturersController(ManufacturerService manufacturers) : ApiController
{
    [HttpGet]
    public Task<PagedResult<ManufacturerDto>> List([FromQuery] PageQuery query, CancellationToken ct) => manufacturers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ManufacturerDto> Get(Guid id, CancellationToken ct) => manufacturers.GetAsync(id, ct);

    [HttpPost, Authorize(Roles = Roles.CentralStaff)]
    public async Task<ActionResult<ManufacturerDto>> Create(SaveManufacturerRequest request, CancellationToken ct)
    {
        var manufacturer = await manufacturers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = manufacturer.Id }, manufacturer);
    }

    [HttpPut("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public Task<ManufacturerDto> Update(Guid id, SaveManufacturerRequest request, CancellationToken ct) => manufacturers.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}"), Authorize(Roles = Roles.CentralStaff)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await manufacturers.DeactivateAsync(id, ct);
        return NoContent();
    }
}
