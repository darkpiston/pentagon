using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Pentagon.Functions.Models;

namespace Pentagon.Functions.Functions;

public class ProcessImageFunction
{
    [Function("ProcessImage")]
    public IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req,
        [FromBody] ProcessImageRequest request)
    {
        throw new NotImplementedException("ProcessImage is not implemented.");
    }
}
