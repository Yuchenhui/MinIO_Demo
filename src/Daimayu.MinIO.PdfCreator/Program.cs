using Aspose.Words;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Daimayu.MinIO.PdfCreator
{
    class Program
    {
        static void Main(string[] args)
        {
            var licenseWords = new Aspose.Words.License();
            licenseWords.SetLicense("Aspose.Total.lic");

            Document doc = new Document();
            var files = Directory.GetFiles("images");
            for(var n = 0; n < files.Length; n++)
            {
                DocumentBuilder db = new DocumentBuilder();
                db.InsertImage(files[n]);
                db.InsertHtml($"<h1>{n}</h1>");
                doc.AppendDocument(db.Document, ImportFormatMode.UseDestinationStyles);
            }
            doc.Save("10242.pdf", SaveFormat.Pdf);
        }
    }
}
