using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Net;
using System.Web;
using System.IO;
using Newtonsoft.Json;
using System.Diagnostics;
using FileGPIO;
using System.Security.Cryptography;
using System.Globalization;
namespace DoorBellClient
{
    class Program
    {
		//FileGPIO accesses the General Purpose Input/Output via the file system.
		//Raspberr pi exposes these pins via special files.
        static FileGPIO.FileGPIO s_Gpio = new FileGPIO.FileGPIO();
        static DateTime startTime = DateTime.Now;
        static string deviceID = "325425423";
        static string photoQuality = "50";
        static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                photoQuality = args[0];
            }

            Console.CancelKeyPress += Console_CancelKeyPress;

            while (true)
            {
                try
                {
					
					//Pin 17 Must be attached to GROUND or low voltage or else the program will stop.
					//This is how we break out of the loop via hardware.
                    if (!s_Gpio.InputPin(FileGPIO.FileGPIO.enumPIN.gpio17))
                    {
						//This send a HIGH voltage (or power) to pin 4. Attaching an LED to pin 4
						//will activate the LED. You should attach a resistor and a green LED to this pin.
                        showReady(true);
                        //Poll the pin checking it over and over. WHen Pin 22 is attached to
						//POWER or High voltage the photo taking process will start. This
						//is how we detect if the doorbell is pressed
                        if (s_Gpio.InputPin(FileGPIO.FileGPIO.enumPIN.gpio22))
                        {
                            showReady(false);
                            TakeAndSendPicture();
                        }
                    }
                    else
                    {
                        showReady(false);
                        //hardware stop button.
                        break;
                    }

                }
                //Never crash. Print the error. Possible connection errors can land us here
                catch (Exception e)
                {
                    showReady(false);
                    Console.WriteLine("Encountered an error. Check this exception " + e);
                    Console.WriteLine("Restarting...");
                }

                System.Threading.Thread.Sleep(100);
            }
        }

        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Environment.Exit(0);
        }

        static void showReady(bool ready)
        {
            s_Gpio.OutputPin(FileGPIO.FileGPIO.enumPIN.gpio4, ready);
        }

        static void TakeAndSendPicture()
        {

#if LINUX
                //Start photo taking process
                //This will kick off the process we'll wait for it after we get the photo url
                Process raspistill = new Process();
                raspistill.StartInfo = new ProcessStartInfo("/usr/bin/raspistill", "-n -q " + photoQuality + " -o /home/pi/Desktop/me.jpg -h 200 -w 200 -t 500")
                {
                    UseShellExecute = false
                };

                raspistill.Start();
#endif

            //Get Photo Url while the rapistill process is taking the picture.
			//This request will ask the server for a new photo blob that we can upload the picture to.
            WebRequest photoRequest = WebRequest.Create("https://smartdoordemo.azure-mobile.net/api/getuploadblobsas");
            photoRequest.Method = "GET";
            photoRequest.Headers.Add("X-ZUMO-APPLICATION", ConfigurationManager.AppSettings["MobileServiceAPIKey"]);
            PhotoResponse photoResp = null;
            using (var sbPhotoResponseStream = photoRequest.GetResponse().GetResponseStream())
            {
                StreamReader sr = new StreamReader(sbPhotoResponseStream);

                string data = sr.ReadToEnd();

                photoResp = JsonConvert.DeserializeObject<PhotoResponse>(data);
            }

			//We've gotten the Shared Access Signature for the blob in URL form.
			//This URL points directly to the blob and we are now authorized to
			//upload the picture to this url with a PUT request
            Console.WriteLine("Pushing photo to SAS Url: " + photoResp.sasUrl);
            WebRequest putPhotoRequest = WebRequest.Create(photoResp.sasUrl);
            putPhotoRequest.Method = "PUT";
            putPhotoRequest.Headers.Add("x-ms-blob-type", "BlockBlob");

#if LINUX
            //wait until the photo was taken.
            raspistill.WaitForExit();
            FileStream fs = new FileStream(@"/home/pi/Desktop/me.jpg", FileMode.Open);
#else
			//for windows, just upload the test photo.
            FileStream fs = new FileStream(@"../../testPhoto.jpg", FileMode.Open);
#endif
			
            using (fs)
            using (var reqStream = putPhotoRequest.GetRequestStream())
            {
                Console.WriteLine("Writing photo to blob...");
                fs.CopyTo(reqStream);
            }

            using (putPhotoRequest.GetResponse())
            {
                Console.WriteLine("Sucessfully Uploaded Photo to cloud");
            }

            Console.WriteLine("Sending notification to service bus queue");
            WebRequest sbRequest = WebRequest.Create("https://smartdoordemo.servicebus.Windows.net/smartdoorqueue/messages");
            var headers = sbRequest.Headers;
            sbRequest.Method = "POST";
            using (var sbMessageStream = sbRequest.GetRequestStream())
            {
                string body = JsonConvert.SerializeObject(new DoorBellNotification()
                {
                    doorBellID = deviceID,
                    imageUrl = photoResp.photoId
                });
                var bytes = Encoding.UTF8.GetBytes(body);
                sbMessageStream.Write(bytes, 0, bytes.Length);
                headers.Add("Authorization", createToken("https://smartdoordemo.servicebus.Windows.net/smartdoorqueue/messages", "DevicePolicy", ConfigurationManager.AppSettings["ServiceBusSharedAccessKey"]));
            }

            try
            {
                Console.WriteLine("Sending door bell notification for " + deviceID);
                using (var response = sbRequest.GetResponse())
                {
                    Console.WriteLine("Sucessfully Sent");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Couldn't post to service bus -" + e);
            }

        }

        // Create REST request and append the token to ‘Authorization’ header . . . 

        /// <summary> 
        /// Code  for generating of SAS token for authorization with Service Bus 
        /// 
        /// This handy function can be found on this helpful blog post:
        /// http://developers.de/blogs/damir_dobric/archive/2013/10/17/how-to-create-shared-access-signature-for-service-bus.aspx
        /// </summary> 
        /// <param name="resourceUri"></param> 
        /// <param name="keyName"></param> 
        /// <param name="key"></param> 
        /// <returns></returns> 
        private static string createToken(string resourceUri, string keyName, string key)
        {
            TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
            var expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + 3600); //EXPIRES in 1h 
            string stringToSign = HttpUtility.UrlEncode(resourceUri) + "\n" + expiry;
            HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));

            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            var sasToken = String.Format(CultureInfo.InvariantCulture,
            "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}",
                HttpUtility.UrlEncode(resourceUri), HttpUtility.UrlEncode(signature), expiry, keyName);

            return sasToken;
        }

    }
	

	/**
		Serialization classes for the JSON coming from the mobile service
	**/
    public class DoorBellNotification
    {
        public string doorBellID { get; set; }
        public string imageUrl { get; set; }
    }

    public class PhotoResponse
    {
        public string sasUrl { get; set; }
        public string photoId { get; set; }
        public string expiry { get; set; }
    }

}
