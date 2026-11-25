namespace SdkNet48
{
    public class Use
    {
        public LegacyLib.Class1 Legacy { get; } = new LegacyLib.Class1();

        public WebApp.Global Application { get; } = new WebApp.Global();

        public string Name => typeof(object).Assembly.GetName().Name;

        public WpfApp.MainWindow Window => Legacy.Window;
    }
}
