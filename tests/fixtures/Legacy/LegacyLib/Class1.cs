using System.Linq;
using System.Web;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace LegacyLib
{
    public class Class1 : Form
    {
        public string Encode(string s) =>
            HttpUtility.HtmlEncode(s) + JsonConvert.SerializeObject(new[] { 1, 2 }.Select(x => x * 2)) + Properties.Resources.Greeting;

        public System.Net.Http.HttpClient Client { get; } = new System.Net.Http.HttpClient();

        public WpfApp.MainWindow Window { get; set; }

        public object Caption => Window.OkButton.Content;
    }
}
