using Asp.Versioning;
using LN_history.Api.Infrastructure;
using LN_history.Api.Mapping;
using LN_history.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LN_history.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/blocks")]
public class BitcoinController : ControllerBase
{
    private readonly IBlockService _blocks;

    public BitcoinController(IBlockService blocks)
    {
        _blocks = blocks;
    }

    /// <summary>Bitcoin block by height.</summary>
    [HttpGet("{height:long}")]
    public async Task<IActionResult> GetByHeight(long height, CancellationToken cancellationToken)
    {
        var block = await _blocks.GetByHeightAsync(height, cancellationToken);
        return block is null
            ? Problem($"Block at height {height} not found.", statusCode: StatusCodes.Status404NotFound)
            : Ok(block.ToDto());
    }

    /// <summary>Bitcoin block by hash.</summary>
    [HttpGet("{hash:regex(^[[0-9a-fA-F]]{{64}}$)}")]
    public async Task<IActionResult> GetByHash(string hash, CancellationToken cancellationToken)
    {
        var block = await _blocks.GetByHashAsync(hash, cancellationToken);
        return block is null
            ? Problem($"Block '{hash}' not found.", statusCode: StatusCodes.Status404NotFound)
            : Ok(block.ToDto());
    }

    /// <summary>The last block mined at or before a timestamp.</summary>
    [HttpGet]
    public async Task<IActionResult> GetByTimestamp(
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseInstant(timestamp, out var instant))
            return Problem($"Invalid or missing timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var block = await _blocks.GetByTimestampAsync(instant, cancellationToken);
        return block is null
            ? Problem($"No block found at or before {instant:o}.", statusCode: StatusCodes.Status404NotFound)
            : Ok(block.ToDto());
    }
}
