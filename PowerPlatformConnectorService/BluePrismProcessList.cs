using System;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Net;


namespace PowerPlatformConnectorService
{
    public static class BluePrismProcessList
    {
        [Produces("application/json")]
        [FunctionName("GetProcessList")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req, ILogger log)
        {
            log.LogInformation("GetProcessList function requested.");

            string resourceName = req.Query["resourceName"];
            string strPort = null;

            if (resourceName.Contains(":"))
            {
                int charPos = resourceName.IndexOf(":");
                strPort = resourceName.Substring(charPos + 1);
                resourceName = resourceName.Substring(0, charPos);
            }
            else { strPort = "8181"; }

            if (resourceName != null)
            {
                HttpWebRequest request = CreateWebRequest(resourceName, strPort);
                WebResponse wr = request.GetResponse();
                StreamReader sr = new StreamReader(wr.GetResponseStream());

                string HTMLresults = sr.ReadToEnd();
                sr.Close();
                sr.Dispose();

                JArray jProcessList = new JArray();
                
                while (HTMLresults.Contains("<b>")) {

                    int beginB = HTMLresults.IndexOf("<b>");
                    int endB = HTMLresults.IndexOf("</b>");
                    string processName = HTMLresults.Substring(beginB + 3, endB - beginB - 3);
                    JObject jProcess = new JObject();
                    jProcess.Add("ProcessName", processName);
                    jProcessList.Add(jProcess);
                    HTMLresults = HTMLresults.Remove(beginB, (endB - beginB) + 4);
                 }

                return (ActionResult)new OkObjectResult(jProcessList);
            } else
            {
                return (ActionResult)new BadRequestObjectResult("Please pass a resourceName  and option port on the query string");
            }
        }
        private static HttpWebRequest CreateWebRequest(string strResource, string strPort)
        {
            string strURI = string.Format(@"http://{0}:{1}/ws/", strResource, strPort);
            HttpWebRequest Req = (HttpWebRequest)WebRequest.Create(strURI);
            //Req.ContentType = "text/xml;charset=\"utf-8\"";
            Req.Accept = "text/html";
            Req.Method = "GET";
            return Req;
        }
    }

}
