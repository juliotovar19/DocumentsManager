using Amazon.CDK;

namespace DocumentsManager.Infra
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();
            new DocumentsManagerStack(app, "DocumentsManagerStack");
            app.Synth();
        }
    }
}