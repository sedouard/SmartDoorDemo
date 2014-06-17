#SmartDoor

Smart door is a demo project which shows how you can use a Raspberry Pi to connect to Azure and power mobile apps via Service Bus messaging and Azure Mobile Services.

This repository consist of 2 of the 3 components. The first is the DoorBellClient program which is the code that runs on the raspberry pi. The second is the Windows Universal application which runs on either Desktop or as a Windows 8 app.

#Running the DoorBellClient on Windows

To Run the DoorBellClient open DoorBellClient/DoorBellClient.sln in Visual Studio 2013. Change the solution configuration to 'Debug'.

In Program.cs change <YOUR MOBILE SERVICE API KEY> with your mobile service API key and <YOUR MOBILE SERVICE NAME> to the name of your Azure mobile service.

Press F5 and the program will repeatedly upload testPhoto.jpg to your storage account. You should place a breakpoint at 'Sucessfully Uploaded Photo to cloud' which will be hit after every upload.

#Running the DoorBellClient on Raspberry Pi

To Run the DoorBellClient open DoorBellClient/DoorBellClient.sln in Visual Studio 2013. Change the solution configuration to 'RaspberryPi'.

Build the solution and in bin/RaspberryPi copy all the contents and place them in a folder on your Raspberry Pi desktop.

Execute the following command on the terminal of the Raspberry Pi:

<b>sudo mono DoorBellClient.exe </b>

The program will immediately start polling the input pins. In order for the program to keep running, Pin17 should be pulled to GROUND and Pin22 should be pulled to GROUND. A 'ready' LED can be attached to the output Pin4 and when you want the device to take a picture, pull Pin22 HIGH.

See the blog post http://stevenedouard.com/running-code-raspberry-pi-send-photos-cloud for more information

