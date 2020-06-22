
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;
using System.Net;
using System;
using System.Xml;
using Newtonsoft.Json.Linq;
using System.Xml.Linq;

namespace PowerPlatformConnectorService
{
    public static class BluePrismRestInterface
    {
        [Produces("application/json")]
        [FunctionName("BluePrismRestInterface")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)]HttpRequest req, TraceWriter log)
        {
            log.Info("Blue Prism Rest Interface function processed a request.");

            //Set up params for starting a process.
            //Format [resourccepc]:[port]
            //Use port 8181 as default if none specified
            string strProcessName = req.Query["processName"];
            string strResourceName = req.Query["resourceName"];
            string strPort = null;

            if (strResourceName.Contains(":")){
                int charPos = strResourceName.IndexOf(":");
                strPort = strResourceName.Substring(charPos+1);
                strResourceName = strResourceName.Substring(0, charPos);
            } else { strPort = "8181"; }


            //Read the REST Request body and turn into JSON Object
            JObject jInput = null;
            using (var reader = new StreamReader(req.Body))
            {
                var body = reader.ReadToEnd();
                if (body != "") {
                    jInput = JObject.Parse(body);
                    log.Info("Received JSON: " + jInput.ToString());
                };
            }

            //Create outbound SOAP Request to Blue Prism Process
            HttpWebRequest request = CreateSOAPWebRequest(strProcessName, strResourceName, strPort);

            //Use the Header Basic Auth token from REST and pass through to SOAP
            string strAuth = req.Headers["Authorization"];
            request.Headers.Add("Authorization", strAuth);
            //check if "empty" JSON was passed in. If so, set to null for proper processing
            if (!jInput.HasValues) jInput = null;
            
            string convertXML = CreateXMLFromJSON(jInput, log);
            log.Info("Converted XML: " + convertXML);
            var soapString = ConstructSoapRequest(strProcessName, convertXML);

            //pretty up the SOAP payload
            var xml_formatted = XDocument.Parse(soapString).ToString();

            log.Info("SOAP: " + xml_formatted);

            XmlDocument SOAPReqBody = new XmlDocument();
            SOAPReqBody.LoadXml(xml_formatted);
            SOAPReqBody.Save(request.GetRequestStream());

            //execute the SOAP request and get response
            XmlDocument SOAPResponseBody = new XmlDocument();
            bool boolSoapFault = false;
            string strSoapErrMsg = null;
            try
            {
                WebResponse wr = request.GetResponse();
                StreamReader sr = new StreamReader(wr.GetResponseStream());
                String strContentType = wr.ContentType;                
                SOAPResponseBody.LoadXml(sr.ReadToEnd());
                sr.Close();
                sr.Dispose();
            } catch (Exception e)
            {
                strSoapErrMsg = e.Message;
                boolSoapFault = true;
                log.Error(strSoapErrMsg);
            }

            //check for SOAP Fault msgs
            var responseErrNode = SOAPResponseBody.GetElementsByTagName("soapString:Fault")[0];
            if (responseErrNode != null)
            {
                boolSoapFault = true;
                strSoapErrMsg = "Err Msg: " + strSoapErrMsg;
            }

            //Get the specific action response from the SOAP Body and turn into JSON
            //The Blue Prism process must be published with document literal formatting
            var responseNode = SOAPResponseBody.GetElementsByTagName(strProcessName + "Response")[0];
            JObject jsonResponse = null;
            if (responseNode != null)
            {
                if (responseNode.HasChildNodes)
                {
                    jsonResponse = new JObject();
                    foreach (XmlNode node in responseNode.ChildNodes)
                    {
                        jsonResponse.Add(node.Name, node.InnerText);
                    }
                }
            } else jsonResponse = null;

            if (boolSoapFault == false)
            {
                return (ActionResult)new OkObjectResult(jsonResponse);
            }
            else return (ActionResult)new BadRequestObjectResult(new { message = strSoapErrMsg, currentDate = DateTime.Now });
        }
        private static HttpWebRequest CreateSOAPWebRequest(string strProcess, string strResource, string strPort)
        {
            string strURI = string.Format(@"http://{0}:{1}/ws/{2}", strResource, strPort, strProcess);
            HttpWebRequest Req = (HttpWebRequest)WebRequest.Create(strURI);
            Req.ContentType = "text/xml;charset=\"utf-8\"";
            Req.Accept = "text/xml";
            Req.Method = "POST";
            return Req;
        }
        private static string ConstructSoapRequest(String processName, String xmlPayload)
        {
            return string.Format(@"<soapenv:Envelope xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:urn=""urn:blueprism:webservice:flowtoblueprism"">
            <soapenv:Header/>
                <soapenv:Body>
                    <{0} soapenv:encodingStyle=""http://schemas.xmlsoap.org/soap/encoding/"">{1}</{0}>
                </soapenv:Body>
                </soapenv:Envelope>", processName, xmlPayload);
        }
        private static string CreateXMLFromJSON(JObject jInput, TraceWriter log)
        {
            if (jInput == null) { return ""; }
            try
            {
                XNode tmpNode = JsonConvert.DeserializeXNode(jInput.ToString(), "Root");
                string strXML = tmpNode.ToString();
                strXML = strXML.Replace("<Root>", null);
                strXML = strXML.Replace("</Root>", null);
                return strXML;
            } catch (Exception e)
            {
                log.Error("Error", e);
                return "";
            }
        }
    }
}
