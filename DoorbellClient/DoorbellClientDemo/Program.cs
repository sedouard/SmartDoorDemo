using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using System.Net;
using System.Web;
using System.IO;
using System.Configuration;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Web;
using Microsoft.ServiceBus.Messaging;
using FileGPIO;
namespace DoorBellClient
{
    class Program
    {
		//FileGPIO accesses the General Purpose Input/Output via the file system.
		//Raspberr pi exposes these pins via special files.
        static FileGPIO.FileGPIO s_Gpio = new FileGPIO.FileGPIO();
        static DateTime startTime = DateTime.Now;
        static int wrapExpSecs = -1;
        static string deviceID = "325425423";
        static string wrapToken = null;
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

            var qc = QueueClient.CreateFromConnectionString(ConfigurationManager.AppSettings["Microsoft.ServiceBus.ConnectionString"], "smartdoorqueue");

            var notificationMessageBody = new DoorBellNotification(){
                doorBellID = ConfigurationManager.AppSettings["DoorbellID"],
                imageUrl = photoResp.sasUrl
            };
            //serialize message body object and convert to byte array
            byte[] messageBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(notificationMessageBody));
            
            //create a Stream from the bytes, we will use it to send to the Service Bus Queue
            var messageStream = new MemoryStream(messageBody);

            try
            {
                qc.Send(new BrokeredMessage(messageStream));
                Console.WriteLine("Sucessfully sent service bus message");
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }

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
