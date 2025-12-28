using ConsoleApp1;
using ImageMagick;
using ImageMagick.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace YmmOcrSistemi
{
    class Program
    {
        static void Main(string[] args)
        {
            string tessData = @"C:\ocr";
            string pdfPath = @"D:\input3.pdf";

            var processor = new TableRegionProcessor(tessData);

            // Kural Tanımı: Kısmi Tevkifat Tablosu - İşlem Türü Sütunu
            //var rule = new OcrRule
            //{
            //    FieldName = "Kısmi Tevkifat İşlem Türü",
            //    StartAnchor = "KISMİ TEVKİFAT UYGULANAN İŞLEMLER",
            //    ColumnHeader = "İşlem Türü",
            //    RightLimitHeader = "Matrah", // Matrah sütununa kadar genişlik
            //    EndAnchor = "Matrah Toplamı",
            //    XOffset = -320, // İşlem türü başlığıyla aynı hizadan başla
            //    Type = ExtractionType.TableColumnList
            //};

            var rule = new OcrRule
            {
                FieldName = "Tevkifat_Uygulanmayan_Vergi_Toplami2",
                StartAnchor = "KISMI TEVKIFAT KAPSAMINA GİREN İŞLEMLER",
                ColumnHeader = "Teslim ve Hizmet Tutarı",
                RightLimitHeader = "İadeye Konu Olan KDV", // Boş bırakıldığında otomatik olarak sayfa sonuna (Sağ taraf) kadar tarar
                RightLimitHeaderXOffset = 100,
                EndAnchor = "İade Edilebilir KDV",
                XOffset = 50,
                ManualWidth = null, // Otomatik hesaplanması için null bırakın
                Type = ExtractionType.TableColumnList
            };

            var settings = new MagickReadSettings { Density = new Density(300, 300) };

            using (var images = new MagickImageCollection())
            {
                images.Read(pdfPath, settings);

                object finalResult = null;
                int foundOnPage = -1;

                // TÜM SAYFALARI DÖNÜYORUZ
                for (int i = 0; i < images.Count; i++)
                {
                    Console.WriteLine($"Sayfa {i + 1} kontrol ediliyor...");
                    var result = processor.ProcessRule(images[i], rule);

                    if (result != null)
                    {
                        finalResult = result;
                        foundOnPage = i + 1;

                        if (result is decimal total)
                        {
                            Console.WriteLine($"{rule.FieldName}: {total:N2} TL");
                        }
                        else if (result is List<string> list)
                        {
                            list.ForEach(x => Console.WriteLine($"Satır: {x}"));
                        }

                        break; // Veriyi bulduğumuz an döngüden çıkıyoruz
                    }
                }

                if (finalResult != null)
                {
                    Console.WriteLine($"Veri {foundOnPage}. sayfada bulundu!");
                    Console.WriteLine($"Sonuç: {finalResult}");
                }
                else
                {
                    Console.WriteLine("Aranan çapalar hiçbir sayfada bulunamadı.");
                }
            }

            Console.ReadLine();
        }
    }
}