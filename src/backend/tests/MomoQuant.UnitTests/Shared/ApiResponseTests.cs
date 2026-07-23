using MomoQuant.Shared.Contracts;

namespace MomoQuant.UnitTests.Shared;

public class ApiResponseTests
{
    [Fact]
    public void Ok_ReturnsSuccessResponse()
    {
        var response = ApiResponse<string>.Ok("value");

        Assert.True(response.Success);
        Assert.Equal("value", response.Data);
        Assert.Equal("Request completed successfully.", response.Message);
    }

    [Fact]
    public void Fail_ReturnsErrorResponse()
    {
        var response = ApiResponse<string>.Fail("Validation failed.");

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Equal("Validation failed.", response.Message);
    }

    [Fact]
    public void PagedResult_CalculatesTotalPages()
    {
        var result = new PagedResult<int>
        {
            Items = [1, 2, 3],
            Page = 1,
            PageSize = 2,
            TotalCount = 5
        };

        Assert.Equal(3, result.TotalPages);
    }
}
