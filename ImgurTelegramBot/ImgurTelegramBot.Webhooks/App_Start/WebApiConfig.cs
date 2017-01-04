using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Web.Http;

namespace ImgurTelegramBot.Webhooks
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            var token = ConfigurationManager.AppSettings["Token"].Split(':')[1];
            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/" + token + "/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );
        }
    }
}
