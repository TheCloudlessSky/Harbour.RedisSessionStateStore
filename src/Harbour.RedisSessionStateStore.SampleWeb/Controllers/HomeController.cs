using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

namespace Harbour.RedisSessionStateStore.SampleWeb.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet]
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public ActionResult Index(string name, int age)
        {
            this.Session["name"] = name;
            this.Session["age"] = age;
            return this.RedirectToAction("index");
        }

        [HttpPost]
        public ActionResult AbandonSession()
        {
            this.Session.Abandon();
            return this.RedirectToAction("index");
        }
    }
}
