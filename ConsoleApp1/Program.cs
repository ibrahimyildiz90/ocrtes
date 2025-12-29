using ConsoleApp1;
using ImageMagick;
using System;
using System.Collections.Generic;

namespace YmmOcrSistemi
{
    class Program
    {
        static void Main(string[] args)
        {
            string tessData = @"C:\ocr";
            string pdfPath = @"D:\input2.pdf";

            // İşlemciyi başlatıyoruz
            var processor = new FieldRegionProcessor(tessData);

            // Geliştirilmiş Kural Tanımı
            var rule = new OcrRule
            {
                StartAnchor = "DİĞER İADE HAKKI DOĞURAN İŞLEMLER",
                FieldName = "Sonraki Döneme Devreden Katma Değer Vergisi",
                EndAnchor = "DİĞER BİLGİLER",
                Type = ExtractionType.TableColumnSum
            };

            var settings = new MagickReadSettings { Density = new Density(300, 300) };

            using (var images = new MagickImageCollection())
            {
                Console.WriteLine("PDF okunuyor, lütfen bekleyin...");
                images.Read(pdfPath, settings);

                decimal totalSum = 0;
                List<string> aggregatedList = new List<string>();
                bool anyDataFound = false;

                Console.WriteLine($"--- {rule.FieldName} Taraması Başladı ---");

                for (int i = 0; i < images.Count; i++)
                {
                    Console.WriteLine($"Sayfa {i + 1} işleniyor...");

                    // Geliştirilmiş OCR metodu burada çağrılıyor
                    var result = processor.ProcessFieldRule(images[i], rule);

                    if (result != null)
                    {
                        anyDataFound = true;
                        if (result is decimal pageTotal)
                        {
                            totalSum += pageTotal;
                            if (pageTotal > 0)
                                Console.WriteLine($"> Sayfa {i + 1} Tutarı: {pageTotal:N2} TL");
                        }
                        else if (result is List<string> pageList)
                        {
                            aggregatedList.AddRange(pageList);
                        }
                    }
                }

                // Sonuç Ekranı
                Console.WriteLine("\n" + new string('=', 40));
                if (anyDataFound)
                {
                    if (rule.Type == ExtractionType.TableColumnSum)
                        Console.WriteLine($"{rule.FieldName} GENEL TOPLAM: {totalSum:N2} TL");
                    else
                        aggregatedList.ForEach(x => Console.WriteLine($"- {x}"));
                }
                else
                {
                    Console.WriteLine("[UYARI] Veri bulunamadı. Çapa metinlerini kontrol edin.");
                }
                Console.WriteLine(new string('=', 40));
            }
            Console.ReadLine();
        }
    }
}