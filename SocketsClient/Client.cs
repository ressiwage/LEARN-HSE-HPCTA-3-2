using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Sockets
{
    public partial class frmMain : Form
    {
        private TcpClient Client = new TcpClient();     // клиентский сокет
        private IPAddress IP;                           // IP-адрес клиента
        string nickname;


        private Int32 PipeHandle;   // дескриптор канала
        private Int32 NicknameNamedPipeHandle;
        private volatile bool _continue = true;

        private Socket ClientSock;                      // клиентский сокет
        private TcpListener Listener;                   // сокет сервера
        private List<Thread> Threads = new List<Thread>();      // список потоков приложения (кроме родительского)

        private Thread t;
        public static bool IsBasicLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }
        public static bool FormatValid(string format)
        {
            if (format.Length == 0) return false;
            foreach (char c in format)
            {
                // This is using String.Contains for .NET 2 compat.,
                //   hence the requirement for ToString()
                if (!IsBasicLetter(c))
                    return false;
            }

            return true;
        }
        public static String getPipeValidName()
        {
            Form prompt = new Form()
            {
                Width = 500,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = "введите никнейм",
                StartPosition = FormStartPosition.CenterScreen
            };
            Label textLabel = new Label() { Left = 50, Top=20, Text="введите никнейм", Width=400 };
            TextBox textBox = new TextBox() { Left = 50, Top=50, Width=400 };
            Button confirmation = new Button() { Text = "Ok", Left=350, Width=100, Top=70 };
            confirmation.Click += (sender, e) => {
                if (FormatValid(textBox.Text))
                {
                    prompt.Close();
                }
                else
                {
                    textLabel.Text = "Некорректный ввод. Никнейм должен содержать только латинские буквы";
                    textLabel.ForeColor = Color.Red;
                }
            };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;
            prompt.ShowDialog();
            String nickname = textBox.Text == "" ? "anon" : textBox.Text;
            return nickname;
        }

        private void ReadMessages(object ClientSock)
        {
            string msg = "";        // полученное сообщение

            // входим в бесконечный цикл для работы с клиентским сокетом
            while (_continue)
            {
                byte[] buff = new byte[1024];                           // буфер прочитанных из сокета байтов
                ((Socket)ClientSock).Receive(buff);                     // получаем последовательность байтов из сокета в буфер buff
                msg = System.Text.Encoding.Unicode.GetString(buff);     // выполняем преобразование байтов в последовательность символов

                messagesTB.Invoke((MethodInvoker)delegate
                {
                    if (msg.Replace("\0", "") != "")
                    {
                        messagesTB.Text += "\n >> " + msg;                             // выводим полученное сообщение на форму
                    }

                    //rtbMessages.Text += "\n >> " + msg;             // выводим полученное сообщение на форму
                });
                Thread.Sleep(500);
            }
        }

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


        // конструктор формы
        public frmMain()
        {
            this.nickname = getPipeValidName();
            InitializeComponent();
            nickBox.Text = "ваш ник: " + this.nickname;

            IPHostEntry hostEntry = Dns.GetHostEntry(Dns.GetHostName());    // информация об IP-адресах и имени машины, на которой запущено приложение
            IP = hostEntry.AddressList[0];                                  // IP-адрес, который будет указан в заголовке окна для идентификации клиента
            int Port = 1011;                                                // порт, который будет указан при создании сокета

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

            Threads.Clear();
            Threads.Add(new Thread(ReceiveMessage));
            Threads[Threads.Count-1].Start();
        }

        // подключение к серверному сокету
        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                int Port = 1010;                                // номер порта, через который выполняется обмен сообщениями
                IPAddress IP = IPAddress.Parse(tbIP.Text);      // разбор IP-адреса сервера, указанного в поле tbIP
                Client.Connect(IP, Port);                       // подключение к серверному сокету
                btnConnect.Enabled = false;
                btnSend.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Введен некорректный IP-адрес" + ex.ToString());
            }
        }

        // отправка сообщения
        private void btnSend_Click(object sender, EventArgs e)
        {
            byte[] buff = Encoding.Unicode.GetBytes(IP.ToString() + " <:> " + this.nickname + " <:> " + tbMessage.Text);   // выполняем преобразование сообщения (вместе с идентификатором машины) в последовательность байт
            Byte[] buff2 = System.BitConverter.GetBytes(buff.Length);
            Stream stm = Client.GetStream();                                                    // получаем файловый поток клиентского сокета
            stm.Write(buff2, 0, buff2.Length);                                                    // выполняем запись последовательности байт
            stm.Write(buff, 0, buff.Length);                                                    // выполняем запись последовательности байт
        }

        private void frmMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            Client.Close();         // закрытие клиентского сокета
        }
    }
}