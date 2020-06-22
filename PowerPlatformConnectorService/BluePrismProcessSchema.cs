using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net;
using System.Xml;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace PowerPlatformConnectorService
{
    public static class BluePrismProcessSchema
    {
        [Produces("application/json")]
        [FunctionName("GetProcessSchema")]
        public static IActionResult Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("GetProcessSchema function requested" );

            string processName = req.Query["processName"];
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
                HttpWebRequest request = CreateWSDLRequest(resourceName, strPort, processName);
                WebResponse wr = request.GetResponse();
                StreamReader sr = new StreamReader(wr.GetResponseStream());
                string WSDLresults = sr.ReadToEnd();
                sr.Close();
                sr.Dispose();

                XmlDocument WSDLResponseBody = new XmlDocument();

                WSDLResponseBody.LoadXml(WSDLresults);
                XmlElement root = WSDLResponseBody.DocumentElement;
                XmlNamespaceManager xmnsmgr = new XmlNamespaceManager(WSDLResponseBody.NameTable);
                xmnsmgr.AddNamespace("s", "http://www.w3.org/2001/XMLSchema");

                string idToFind = processName;
                XmlNode selectedInputElement = root.SelectSingleNode("//s:element[@name='" + idToFind + "']/s:complexType/s:sequence", xmnsmgr);

                JObject jsonResponse = new JObject();
                jsonResponse.Add("$id", "https://example.com/person.schema.json");
                jsonResponse.Add("$schema", "http://json-schema.org/draft-07/schema#");
                jsonResponse.Add("type", "object");
                JObject schemaInputProps = new JObject();
                JObject schemaOutputProps = new JObject();
                if (selectedInputElement.HasChildNodes)
                {
                    foreach (XmlNode node in selectedInputElement.ChildNodes)
                    {
                        string xmlName = node.Attributes.GetNamedItem("name").Value;
                        string xmlType = node.Attributes.GetNamedItem("type").Value;
                        xmlType = xmlType.Substring(xmlType.IndexOf(":") + 1);
                        schemaInputProps.Add(xmlName, xmlType);
                    }
                }

                XmlNode selectedOutputElement = root.SelectSingleNode("//s:element[@name='" + idToFind + "Response']/s:complexType/s:sequence", xmnsmgr);
                JArray jsonOutput = null;
                if (selectedOutputElement.HasChildNodes)
                {
                    jsonOutput = new JArray();
                    foreach (XmlNode node in selectedOutputElement.ChildNodes)
                    {
                        string xmlName = node.Attributes.GetNamedItem("name").Value;
                        string xmlType = node.Attributes.GetNamedItem("type").Value;
                        xmlType = xmlType.Substring(xmlType.IndexOf(":") + 1);
                        schemaOutputProps.Add(xmlName, xmlType);
                    }
                }

                JObject jProps = new JObject();
                JObject jInput = new JObject();
                jInput.Add("type", "object");
                jInput.Add("properties", schemaInputProps);

                JObject jOutput = new JObject();
                jOutput.Add("type", "object");
                jOutput.Add("properties", schemaOutputProps);


                jProps.Add("ProcessInputs", jInput);
                jProps.Add("ProcessOutputs", jOutput);
                jsonResponse.Add("schema", jProps);

                return (ActionResult)new OkObjectResult(jsonResponse);
            } else return (ActionResult)new BadRequestObjectResult("Please pass a resourceName  and option port on the query string");
        }
        private static HttpWebRequest CreateWSDLRequest(string strResource, string strPort, string strProcess)
        {
            string strURI = string.Format(@"http://{0}:{1}/ws/{2}?wsdl", strResource, strPort, strProcess);
            HttpWebRequest Req = (HttpWebRequest)WebRequest.Create(strURI);
            Req.Accept = "text/html";
            Req.Method = "GET";
            return Req;
        }
    }
}
