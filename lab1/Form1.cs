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
    public partial class Form1 : Form
    {
        private FileTransferManager ftm;
        public Form1()
        {
            InitializeComponent();
            ftm = new FileTransferManager((string filename, bool direction) => { return (int)this.Invoke(new Func<string, bool, int>((string s, bool b) => { return (dataGridView1.Rows.Add(s, b ? "->" : "<-", "0", "?")); ; }), filename, direction); },
                (int index, int percentage) => { this.Invoke(new Action<int, int>((i, p) => dataGridView1.Rows[i].Cells[2].Value = p.ToString()), index, percentage); },
                (int i) => { this.Invoke(new Action<int>((ti) => { dataGridView1.Rows[ti].Cells[2].Value = "Done"; dataGridView1.Rows[ti].Cells[3].Value = "0"; }), i); },
                (int i) => { this.Invoke(new Action<int>((ti) => { dataGridView1.Rows[ti].Cells[2].Value = "Failed"; dataGridView1.Rows[ti].Cells[3].Value = "never"; }), i); });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            OpenFileDialog fd = new OpenFileDialog();
            var res = fd.ShowDialog(this);
            if (res == System.Windows.Forms.DialogResult.OK)
            {
                var filename = fd.FileName;
                FileTransferDialog.show(filename, this, ftm);
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                int res;
                if (!int.TryParse(textBox1.Text, out res))
                {
                    MessageBox.Show("\tВведите число\t");
                }
                else
                {
                    ftm.startRecieving(res);
                }
            }
            else
            {
                ftm.stopRecieving();
            }
        }
    }
}
