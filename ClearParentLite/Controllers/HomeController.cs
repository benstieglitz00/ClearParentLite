using System.Diagnostics;
using ClearParentLite.Models;
using Microsoft.AspNetCore.Mvc;

namespace ClearParentLite.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet("/resources")]
        public IActionResult Resources() => View();
        [Route("/status/{code:int}"), IgnoreAntiforgeryToken]
        public IActionResult StatusPage(int code) { Response.StatusCode = code is >=400 and <=599 ? code : 404; ViewBag.StatusCode=Response.StatusCode; return View(); }
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
