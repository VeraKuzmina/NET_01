using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace lab1
{
    public partial class FileTransferDialog : Form
    {
        private FileTransferManager ftm;
        private string filename;
        public FileTransferDialog(FileTransferManager ftm, string filename)
        {
            InitializeComponent();
            this.ftm = ftm;
            this.filename = filename;
        }

        public static void show(string filename, Form parent, FileTransferManager ftm)
        {
            FileTransferDialog ftd = new FileTransferDialog(ftm, filename);
            ftd.Show(parent);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            int port;
            if (!int.TryParse(textBox2.Text, out port))
            {
                MessageBox.Show("\tВведите число\t");
            }
            else
            {
                ftm.transferFile(filename, textBox1.Text, int.Parse(textBox2.Text));
                this.Close();
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
