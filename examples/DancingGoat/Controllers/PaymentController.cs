using Microsoft.AspNetCore.Mvc;

namespace DancingGoat.Controllers;

/// <summary>
/// Controller for handling payment success and cancellation pages.
/// </summary>
public class PaymentController : Controller
{
    [HttpGet]
    [Route("{languageName}/payment-success")]
    public IActionResult PaymentSuccess(string orderNumber)
    {
        ViewBag.OrderNumber = orderNumber;
        return View();
    }

    [HttpGet]
    [Route("{languageName}/payment-cancelled")]
    public IActionResult PaymentCancelled(string orderNumber)
    {
        ViewBag.OrderNumber = orderNumber;
        return View();
    }
}
