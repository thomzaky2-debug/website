using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace NileShop.Filters;

/// <summary>
/// MVC actions returning NotFound() produce an empty 404 body that the
/// status-code middleware cannot replace (the response already carries a
/// Content-Length), leaving users on a blank white page. Render the shared
/// NotFound view instead so navigation and recovery links are always present.
/// </summary>
public sealed class NotFoundPageFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var isNotFound = context.Result switch
        {
            NotFoundResult => true,
            StatusCodeResult status => status.StatusCode == StatusCodes.Status404NotFound,
            ObjectResult obj => obj.StatusCode == StatusCodes.Status404NotFound,
            _ => false
        };
        if (!isNotFound) return;
        context.Result = new ViewResult { ViewName = "/Views/Home/NotFound.cshtml", StatusCode = StatusCodes.Status404NotFound };
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
