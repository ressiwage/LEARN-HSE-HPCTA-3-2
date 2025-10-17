using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Sockets
{
    public partial class frmMain : Form
    {
        private Socket ClientSock;                      // клиентский сокет
        private TcpListener Listener;                   // сокет сервера
        private List<Thread> Threads = new List<Thread>();      // список потоков приложения (кроме родительского)
        private bool _continue = true;                          // флаг, указывающий продолжается ли работа с сокетами
        private List<string> clients = new List<string>();

        public uint SendToPipe(string message, string pipe)
        {
            pipe = pipe.Split(':')[0];
            TcpClient Client = new TcpClient();     // клиентский сокет
            try
            {
                int Port = 1011;                                // номер порта, через который выполняется обмен сообщениями
                IPAddress IP = IPAddress.Parse(pipe);      // разбор IP-адреса сервера, указанного в поле tbIP
                Client.Connect(IP, Port);                       // подключение к серверному сокету
            }
            catch (Exception ex)
            {
                return 0;
            }
            byte[] buff = Encoding.Unicode.GetBytes(message);   // выполняем преобразование сообщения (вместе с идентификатором машины) в последовательность байт
            Stream stm = Client.GetStream();                                                    // получаем файловый поток клиентского сокета
            stm.Write(buff, 0, buff.Length);
            stm.Flush();
            Client.Close();
            return 1;
        }
        private string pipesToText(List<string> clients)
        {
            return String.Join("\n",
                                    (new List<string> { "participants" }).Concat(
                                        clients.Select(
                                            x => {
                                                string[] splitted = x.Split(new string[] { ":" }, StringSplitOptions.None);
                                                return splitted[1];
                                            }
                                            ).ToArray()
                                        ).ToArray());
        }

        // конструктор формы
        public frmMain()
        {
            InitializeComponent();

            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());    // информация об IP-адресах и имени машины, на которой запущено приложение
            IPAddress IP = hostEntry.AddressList[0];                        // IP-адрес, который будет указан при создании сокета
            int Port = 1010;                                                // порт, который будет указан при создании сокета

            // определяем IP-адрес машины в формате IPv4
            foreach (IPAddress address in hostEntry.AddressList)
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    IP = address;
                    break;
                }

            // вывод IP-адреса машины и номера порта в заголовок формы, чтобы можно было его использовать для ввода имени в форме клиента, запущенного на другом вычислительном узле
            this.Text += "     " + IP.ToString() + "  :  " + Port.ToString();

            // создаем серверный сокет (Listener для приема заявок от клиентских сокетов)
            Listener = new TcpListener(IP, Port);
            Listener.Start();

            // создаем и запускаем поток, выполняющий обслуживание серверного сокета
            Threads.Clear();
            Threads.Add(new Thread(ReceiveMessage));
            Threads[Threads.Count-1].Start();
        }

        // работа с клиентскими сокетами
        private void ReceiveMessage()
        {
            // входим в бесконечный цикл для работы с клиентскими сокетом
            while (_continue)
            {
                ClientSock = Listener.AcceptSocket();           // получаем ссылку на очередной клиентский сокет
                Threads.Add(new Thread(ReadMessages));          // создаем и запускаем поток, обслуживающий конкретный клиентский сокет
                Threads[Threads.Count - 1].Start(ClientSock);
            }
        }

        // получение сообщений от конкретного клиента
        private void ReadMessages(object ClientSock)
        {
            string msg = "";        // полученное сообщение

            // входим в бесконечный цикл для работы с клиентским сокетом
            while (_continue)
            {
                byte[] buffAmount = new byte[4];
                ((Socket)ClientSock).Receive(buffAmount);                     // получаем последовательность байтов из сокета в буфер buff
                Int32 msgLen = System.BitConverter.ToInt32(buffAmount, 0);
                byte[] buff = new byte[msgLen];                           // буфер прочитанных из сокета байтов
                ((Socket)ClientSock).Receive(buff);                     // получаем последовательность байтов из сокета в буфер buff
                msg = System.Text.Encoding.Unicode.GetString(buff);     // выполняем преобразование байтов в последовательность символов
                
                rtbMessages.Invoke((MethodInvoker)delegate
                {
                    if (msg.Replace("\0","") != "")
                    {
                        Console.WriteLine(msg);
                        string[] data = msg.Split(new string[] { " <:> " }, StringSplitOptions.None);
                        string clientpipename = data[0]+":"+data[1];
                        if (!clients.Contains(clientpipename))
                        {
                            clients.Add(clientpipename);
                            rtbParticipants.Text = pipesToText(clients);
                        }
                        DateTime dt = DateTime.Now;
                        string time = dt.Hour + ":" + dt.Minute+":"+dt.Second;

                        string message = "\n >> "  + data[0] + "|" + data[1] + "|" + time  + ":  " + data[2];                             // выводим полученное сообщение на форму
                        rtbMessages.Text += message;
                        List<string> delete = new List<string>();
                        foreach (string pipe in clients)
                        {
                            Console.WriteLine("?:>"+message+"<?:"+pipe);
                            
                            if (SendToPipe(message, pipe) == 0)
                            {
                                delete.Add(pipe);
                            }
                        }
                        foreach (var pipe in delete)
                        {
                            clients.Remove(pipe);
                            rtbParticipants.Text = pipesToText(clients);
                        }
                    }

                        //rtbMessages.Text += "\n >> " + msg;             // выводим полученное сообщение на форму
                });
                Thread.Sleep(500);
            }
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            _continue = false;      // сообщаем, что работа с сокетами завершена
            
            // завершаем все потоки
            foreach (Thread t in Threads)
            {
                t.Abort();
                t.Join(500);
            }

            // закрываем клиентский сокет
            if (ClientSock != null)
                ClientSock.Close();

            // приостанавливаем "прослушивание" серверного сокета
            if (Listener != null)
                Listener.Stop();
        }
    }
}