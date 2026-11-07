using System.Diagnostics;
using Fulfillment.ConfigService.Contracts;
using Fulfillment.ConfigService.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.ConfigService.Controllers;

[ApiController]
public sealed class ZonesController(ZonesService service) : ControllerBase
{
    [HttpGet("/zones")]
    [ProducesResponseType<ZoneLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ZoneLookupResponse>> GetZone([FromQuery] ZoneLookupRequest request, CancellationToken cancellationToken)
    {
        var result = await service.GetZoneAsync(request, cancellationToken);
        if (result.Value is not null) return Ok(result.Value);
        return NotFound(new ApiErrorResponse(result.ErrorCode!, result.Message!, null, Activity.Current?.TraceId.ToString()));
    }
}
