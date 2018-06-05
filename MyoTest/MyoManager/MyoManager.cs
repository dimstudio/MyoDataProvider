using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using MyoSharp.Communication;
using MyoSharp.Device;
using MyoSharp.Exceptions;
using MyoSharp.Poses;
using System.Net.Sockets;
using System.Net;
using System.Windows.Media.Media3D;

namespace MyoTest.MyoManager
{
    class MyoManager
    {
        IChannel channel;
        public static IHub hub;
        MainWindow mWindow;

        private int gripEMG = 0;
        private float orientationW=0;
        private float orientationX=0;
        private float orientationY=0;
        private float orientationZ=0;
        private float accelerometerX = 0;
        private float accelerometerY = 0;
        private float accelerometerZ = 0;
        private float gyroscopeX = 0;
        private float gyroscopeY = 0;
        private float gyroscopeZ = 0;

        private DateTime lastExecutionEmg;
        private DateTime lastExecutionVibrate;
        private DateTime lastExecutionOrientation;
        private DateTime lastExecutionAccelerometer;
        private DateTime lastExecutionGyroscope;


        float[] preEmgValue = new float[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
        float[] storeEmgValue = new float[8] { 0, 0, 0, 0, 0, 0, 0, 0 };

        public static ConnectorHub.ConnectorHub myConnector;
        public ConnectorHub.FeedbackHub myFeedback;
        private bool vibrateMyo = true;
        private bool _isRecording = false;
        public bool IsRecording
        {
            get { return _isRecording; }
            set
            {
                _isRecording = value;
            }
        }


        public MyoManager()
        {
            myConnector = new ConnectorHub.ConnectorHub();
            myConnector.init();
            myFeedback = new ConnectorHub.FeedbackHub();
            myFeedback.init();
            myConnector.sendReady();
        }
        

        public void InitMyoManagerHub(MainWindow m)
        {
            lastExecutionEmg = DateTime.Now;
            lastExecutionVibrate = DateTime.Now;
            this.mWindow = m;
            channel = Channel.Create( ChannelDriver.Create(ChannelBridge.Create(),
                MyoErrorHandlerDriver.Create(MyoErrorHandlerBridge.Create())));
            hub = Hub.Create(channel);

            // listen for when the Myo connects
            hub.MyoConnected += (sender, e) =>
            {
                Debug.WriteLine("Myo {0} has connected!", e.Myo.Handle);
                e.Myo.Vibrate(VibrationType.Short);
                e.Myo.EmgDataAcquired += Myo_EmgDataAcquired;
                e.Myo.OrientationDataAcquired += Myo_OrientationAcquired;
                e.Myo.AccelerometerDataAcquired += Myo_AccelerometerAcquired;
                e.Myo.GyroscopeDataAcquired += Myo_GyroscopeAcquired;
                e.Myo.SetEmgStreaming(true);
            };

            // listen for when the Myo disconnects
            hub.MyoDisconnected += (sender, e) =>
            {
                Debug.WriteLine("Oh no! It looks like {0} arm Myo has disconnected!", e.Myo.Arm);
                e.Myo.SetEmgStreaming(false);
                e.Myo.EmgDataAcquired -= Myo_EmgDataAcquired;
            };

            try
            {
                setValueNames();
                myFeedback.feedbackReceivedEvent += MyFeedback_feedbackReceivedEvent;
            }
            catch (Exception e)
            {
                Debug.WriteLine("MyoManager error at connecting the hub");
            }

            // start listening for Myo data
            channel.StartListening();

        }

        #region Send data
        public void setValueNames()
        {
            List<string> names = new List<string>();
            names.Add("orientationW");
            names.Add("orientationX");
            names.Add("orientationY");
            names.Add("orientationZ");
            names.Add("accelerometerX");
            names.Add("accelerometerY");
            names.Add("accelerometerZ");
            names.Add("gyroscopeX");
            names.Add("gyroscopeY");
            names.Add("gyroscopeZ");
            for (int i=0;i<8;i++ )
            {
                names.Add("EMGpod" + i);
            }
            myConnector.setValuesName(names);

        }
        #endregion

        #region MyoEvents
        private void Myo_EmgDataAcquired(object sender, EmgDataEventArgs e)
        {
            
            if (_isRecording == true)
            {
                if ((DateTime.Now - lastExecutionEmg).TotalSeconds >= 0.5)
                {

                    CalculateEMGValues(e);
                    SendData();
                    lastExecutionEmg = DateTime.Now;
                }

                //vibrate only twice a sec
                if (vibrateMyo == true)
                {
                    if (gripEMG >= 4)
                    {
                        Debug.WriteLine("gripEmg" + gripEMG);
                        pingMyo();
                        try
                        {
                            myConnector.sendFeedback("Read Grip the pen gently");
                        }
                        catch
                        {
                            Debug.WriteLine("feedback not sent");
                        }

                        lastExecutionVibrate = DateTime.Now;
                        vibrateMyo = false;
                    }
                }
                if ((DateTime.Now - lastExecutionVibrate).TotalSeconds >= 0.5)
                {
                    vibrateMyo = true;

                }

                gripEMG = 0;

            }
        }

        private void Myo_OrientationAcquired(object sender, OrientationDataEventArgs e)
        {
            if (_isRecording == true)
            {
                if ((DateTime.Now - lastExecutionOrientation).TotalSeconds >= 0.5)
                {
                    CalculateOrientation(e);
                    if (MainWindow.isRecordingData == true){
                        SendData();
                    }
                    lastExecutionOrientation = DateTime.Now;
                }
            }
        }

        private void Myo_AccelerometerAcquired(object sender, AccelerometerDataEventArgs e)
        {
            if (_isRecording == true)
            {
                if ((DateTime.Now - lastExecutionAccelerometer).TotalSeconds >= 0.5)
                {
                    CalculateAccelerometer(e);
                    if (MainWindow.isRecordingData == true){
                        SendData();
                    }

                    lastExecutionAccelerometer = DateTime.Now;
                }
            }
        }

        private void Myo_GyroscopeAcquired(object sender, GyroscopeDataEventArgs e)
        {
            if (_isRecording == true)
            {
                if ((DateTime.Now - lastExecutionGyroscope).TotalSeconds >= 0.5)
                {
                    CalculateGyroscope(e);
                    if (MainWindow.isRecordingData == true){
                        SendData();
                    }

                    lastExecutionGyroscope = DateTime.Now;
                }
            }
        }
        #endregion

        /// <summary>
        /// Method to broadcast packets of data
        /// </summary>
        /// <param name="pressure"></param>
        public void SendData()
        {
            try
            {
                List<string> values = new List<string>();
                values.Add(orientationW.ToString());
                values.Add(orientationX.ToString());
                values.Add(orientationY.ToString());
                values.Add(orientationZ.ToString());
                values.Add(accelerometerX.ToString());
                values.Add(accelerometerY.ToString());
                values.Add(accelerometerZ.ToString());
                values.Add(gyroscopeX.ToString());
                values.Add(gyroscopeY.ToString());
                values.Add(gyroscopeZ.ToString());
                for (int i= 0; i < 8; i++)
                {
                    values.Add(storeEmgValue[i].ToString());
                }
                myConnector.storeFrame(values);
                Debug.WriteLine("MyoManager.values"+values.Count);
                Debug.WriteLine("MyoManager/ The size of value: " + values.Count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
            }
            
    }

        /// <summary>
        /// Iterate through each emg sensor in myo and assign 1 if the sum of the first and second frame of emg has a sum of more than 20.
        /// else assign 0. It means that much variation(100 to -100) was observed propotional to higher tension in muscle. 
        /// </summary>
        /// <param name="e"></param>
        void CalculateEMGValues(EmgDataEventArgs e)
        {
            float[] currentEmgValue = new float[8] { 0, 0, 0, 0, 0, 0, 0, 0 };
       
            for (int i = 0; i <= 7; i++)
            {
                
                try
                {
                    storeEmgValue[i] = ((float)e.EmgData.GetDataForSensor(i))/100;      
                }
                catch
                {
                    Debug.WriteLine("No emg value");
                }
            }
            try
            {
                mWindow.UpdateEMG(storeEmgValue);
            } catch
            {
                Debug.WriteLine("No emg value");
            }

        }

        /// <summary>
        /// Method called upon receiving the even myodata received. It passes on the orientation data to the UpdateOrientation class in Mainwindow
        /// </summary>
        /// <param name="e"></param>
        public void CalculateOrientation(OrientationDataEventArgs e)
        {
            orientationW = e.Orientation.W;
            orientationX = e.Orientation.X;
            orientationY = e.Orientation.Y;
            orientationZ = e.Orientation.Z;
            mWindow.UpdateOrientation(orientationW, orientationX, orientationY, orientationZ);
        }

        /// <summary>
        /// Method called upon receiving the even myodata received. It passes on the orientation data to the UpdateOrientation class in Mainwindow
        /// </summary>
        /// <param name="e"></param>
        public void CalculateAccelerometer(AccelerometerDataEventArgs e)
        {
            accelerometerX = e.Accelerometer.X/5;
            accelerometerY = e.Accelerometer.Y/5;
            accelerometerZ = e.Accelerometer.Z/5;
            mWindow.UpdateAccelerometer(accelerometerX, accelerometerY, accelerometerZ);
        }


        /// <summary>
        /// Method called upon receiving the even myodata received. It passes on the orientation data to the UpdateOrientation class in Mainwindow
        /// </summary>
        /// <param name="e"></param>
        public void CalculateGyroscope(GyroscopeDataEventArgs e)
        {
            gyroscopeX = e.Gyroscope.X/500;
            gyroscopeY = e.Gyroscope.Y/500;
            gyroscopeZ = e.Gyroscope.Z/500;
            mWindow.UpdateGyroscope(gyroscopeX, gyroscopeY, gyroscopeZ);
        }


        public static void pingMyo()
        {
            hub.Myos.Last().Vibrate(VibrationType.Short);
        }

        private void MyFeedback_feedbackReceivedEvent(object sender, string feedback)
        {
            mWindow.UpdateDebug("Myo: Learninghublistener feedback received: " + feedback);
            //Debug.WriteLine("Myo: Learninghublistener feedback received: " + feedback);

            ReadStream(feedback);
        }

        private void ReadStream(String s)
        {
            if (s.Contains("Myo"))
            {
                MyoTest.MyoManager.MyoManager.pingMyo();
                mWindow.UpdateDebug(s);
            }

        }
    }
}
