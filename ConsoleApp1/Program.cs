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
            string pdfPath = @"D:\input1.pdf";

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
                FieldName = "Tevkifat_Uygulanmayan_Vergi_Toplami",
                StartAnchor = "TEVKİFAT UYGULANMAYAN İŞLEMLER",
                ColumnHeader = "Vergi",
                RightLimitHeader = "", // Boş bırakıldığında otomatik olarak sayfa sonuna kadar tarar
                EndAnchor = "KISMİ TEVKİFAT UYGULANAN İŞLEMLER",
                XOffset = 0,
                ManualWidth = null, // Otomatik hesaplanması için null bırakın
                Type = ExtractionType.TableColumnSum
            };

            using (var images = new MagickImageCollection())
            {
                images.Read(pdfPath, new MagickReadSettings { Density = new Density(300) });

                var result = processor.ProcessRule(images[0], rule);

                if (result is decimal total)
                {
                    Console.WriteLine($"{rule.FieldName}: {total:N2} TL");
                }
                else if (result is List<string> list)
                {
                    list.ForEach(x => Console.WriteLine($"Satır: {x}"));
                }
            }

            Console.ReadLine();
        }
        static decimal CalculateTotal(List<string> values)
        {
            decimal total = 0;
            foreach (var v in values)
            {
                // Regex ile sadece rakam, nokta ve virgülü al (diğer her şeyi temizle)
                string clean = Regex.Replace(v, @"[^0-9,\.]", "");

                if (string.IsNullOrEmpty(clean)) continue;

                try
                {
                    // Türkiye formatı: 86.508,76 -> 86508.76 çevrimi
                    // Önce binlik ayıracı (nokta) sil, sonra virgülü noktaya çevir
                    if (clean.Contains(",") && clean.Contains("."))
                        clean = clean.Replace(".", "");

                    clean = clean.Replace(",", ".");

                    if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal d))
                        total += d;
                }
                catch { /* Hatalı satırı atla */ }
            }
            return total;
        }

    }
}