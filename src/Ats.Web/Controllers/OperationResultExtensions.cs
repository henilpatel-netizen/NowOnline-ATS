using Ats.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Ats.Web.Controllers;

// The "TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? msg : result.Error"
// idiom was copy-pasted across five controllers (QUAL-5). Hand-typing the key names and repeating the
// double ternary is easy to get subtly wrong, and the alert partial only reads these two keys.
//
// Deliberately a small extension method rather than a controller base class or a filter pipeline:
// the actions keep their own redirect targets and route values.
public static class OperationResultExtensions
{
    // Standard outcome message: the supplied text on success, the result's own error otherwise.
    public static void SetResultMessage(this Controller controller, OperationResult result, string successMessage)
    {
        controller.TempData[result.Succeeded ? "Success" : "Error"] =
            result.Succeeded ? successMessage : result.Error;
    }
}
