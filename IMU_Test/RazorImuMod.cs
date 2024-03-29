﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO.Ports;

namespace IMU_Test {
    class RazorImuMod {
        private readonly SerialPort _port;
        private readonly byte[] IMU_buffer = new byte[30];
        private IMUState IMU_step = IMUState.ReadHeader1;
        private byte payload_length = 0;
        private byte payload_counter = 0;

        //IMU Checksum
        private byte ck_a = 0;
        private byte ck_b = 0;
        private byte IMU_ck_a = 0;
        private byte IMU_ck_b = 0;
        private int imu_payload_error_count;
        private int imu_checksum_error_count;
        private int imu_messages_received;

        private bool imu_ok;
        private bool imuAnalogs_ok;

        private double roll_sensor;
        private double pitch_sensor;
        private double ground_course;

        private double analog_x;
        private double analog_y;
        private double analog_z;
        private double acc_x;
        private double acc_y;
        private double acc_z;

        public RazorImuMod(string port) {
            _port = new SerialPort(port, 57600);
            //_port = new SerialPort(port, 38400);

            this._port.ReadTimeout = 0;
            this._port.ReceivedBytesThreshold = 14;
            this._port.DiscardNull = false;
            this._port.ErrorReceived += PortErrorReceived;
            this._port.DtrEnable = true;
            this._port.Open();  // If you open the port after you set the event you will endup with problems
            this._port.DiscardInBuffer();
            this._port.DataReceived += PortDataReceived;
        }

        private void PortErrorReceived(object sender, SerialErrorReceivedEventArgs e) {
            Console.WriteLine("Error: {0}", e.EventType);
        }
        
        private void PortDataReceived(object sender, SerialDataReceivedEventArgs e) {
            int numc = 0;
            byte message_num = 0;
            byte data;

            numc = _port.BytesToRead;
            if (numc > 0) {
                for (int i = 0; i < numc; i++) {	// Process bytes received
                    data = (byte)_port.ReadByte();
                    switch (IMU_step) {	 	//Normally we start from zero. This is a state machine
                        case IMUState.ReadHeader1:
                            if (data == 0x44) {
                                IMU_step++; //First byte of data packet header is correct, so jump to the next step
                            } 
                            break;

                        case IMUState.ReadHeader2:
                            if (data == 0x49) {
                                IMU_step++;	//Second byte of data packet header is correct
                            }
                            else {
                                IMU_step = IMUState.ReadHeader1;		 //Second byte is not correct so restart to step zero and try again.	
                            }
                            break;

                        case IMUState.ReadHeader3:
                            if (data == 0x59) {
                                IMU_step++;	//Third byte of data packet header is correct
                            }
                            else {
                                IMU_step = IMUState.ReadHeader1;		 //Third byte is not correct so restart to step zero and try again.
                            }
                            break;

                        case IMUState.ReadHeader4:
                            if (data == 0x64) {
                                IMU_step++;	//Fourth byte of data packet header is correct, Header complete
                            }
                            else {
                                IMU_step = IMUState.ReadHeader1;		 //Fourth byte is not correct so restart to step zero and try again.
                            }
                            break;
                        case IMUState.ReadPayloadLength:
                            payload_length = data;
                            checksum(payload_length);
                            IMU_step++;
                            if (payload_length > 28) {
                                IMU_step = IMUState.ReadHeader1;	 //Bad data, so restart to step zero and try again.		 
                                payload_counter = 0;
                                ck_a = 0;
                                ck_b = 0;
                                imu_payload_error_count++;
                            }
                            break;

                        case IMUState.ReadMessageID:
                            message_num = data;
                            checksum(data);
                            IMU_step++;
                            break;

                        case IMUState.ReadPayload:	// Payload data read...
                            // We stay in this state until we reach the payload_length
                            IMU_buffer[payload_counter] = data;
                            checksum(data);
                            payload_counter++;
                            if (payload_counter >= payload_length) {
                                IMU_step++;
                            }
                            break;
                        case IMUState.ReadChecksum1:
                            IMU_ck_a = data;	 // First checksum byte
                            IMU_step++;
                            break;
                        case IMUState.ReadChecksum2:
                            IMU_ck_b = data;	 // Second checksum byte

                            // We end the IMU/GPS read...
                            // Verify the received checksum with the generated checksum.. 
                            if ((ck_a == IMU_ck_a) && (ck_b == IMU_ck_b)) {
                                if (message_num == 0x02) {
                                    IMU_join_data();
                                }
                                else if (message_num == 0x03) {
                                    GPS_join_data();
                                }
                                else if (message_num == 0x04) {
                                    IMU2_join_data();
                                }
                                else if (message_num == 0x05) {
                                    IMUAnalogs_join_data();
                                }
                                else {
                                    Console.WriteLine("Invalid message number = {0}", message_num);
                                }
                            }
                            else {
                                Console.WriteLine("MSG Checksum error"); //bad checksum
                                imu_checksum_error_count++;
                            }
                            // Variable initialization
                            IMU_step = IMUState.ReadHeader1;
                            payload_counter = 0;
                            ck_a = 0;
                            ck_b = 0;
                            break;
                    }
                }// End for...

            }
        }

        private void checksum(byte data) {
            ck_a += data;
            ck_b += ck_a; 
        }

        private void IMU_join_data() {
            imu_messages_received++;
            int j = 0;

            //Storing IMU roll
            roll_sensor = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]) / 100.0;

            //Storing IMU pitch
            pitch_sensor = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]) / 100.0;

            //Storing IMU heading (yaw)
            ground_course = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]) / 100.0;

            imu_ok = true;
        }

        private void IMUAnalogs_join_data() {
            imu_messages_received++;
            int j = 0;

            // Analog x
            analog_x = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]);
            // Analog y
            analog_y = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]);
            // Analog z
            analog_z = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]);

            // Acc x
            acc_x = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]);
            // Acc y
            acc_y = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]);
            // Acc z
            acc_z = (short)((IMU_buffer[j++] << 8) | IMU_buffer[j++]);

            imuAnalogs_ok = true;
        }

        private void IMU2_join_data() {
            throw new NotImplementedException();
        }

        private void GPS_join_data() {
            throw new NotImplementedException();
        }

        private enum IMUState {
            ReadHeader1 = 0,
            ReadHeader2 = 1,
            ReadHeader3 = 2,
            ReadHeader4 = 3,
            ReadPayloadLength = 4,
            ReadMessageID = 5,
            ReadPayload = 6,
            ReadChecksum1 = 7,
            ReadChecksum2 = 8
        }
    }
}
