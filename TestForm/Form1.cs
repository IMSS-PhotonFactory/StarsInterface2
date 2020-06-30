using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using STARS;

namespace TestForm
{
    public partial class Form1 : Form
    {
        private StarsInterface stars;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            stars = new StarsInterface("term1", "127.0.0.1") { KeyWord = "stars" };
            stars.Connect();
            stars.DataReceived += Stars_DataReceived;
            stars.CallbackOn();
            //StarsCbHandler cb = new StarsCbHandler(handler);
            //stars.StartCbHandler(cb);

        }

        private void Stars_DataReceived(object sender, StarsCbArgs e)
        {
            System.Diagnostics.Debug.WriteLine(e.STARS.allMessage);
        }

        private void handler(object sender, StarsCbArgs ev)
        {
            //System.Diagnostics.Debug.WriteLine(ev.allMessage);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            stars.Send("term2 flushdatatome");
        }
    }
}
