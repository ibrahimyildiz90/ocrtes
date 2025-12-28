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
            string pdfPath = @"D:\input2.pdf";

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
                FieldName = "indirimler",
                StartAnchor = "İNDİRİMLER",
                ColumnHeader = "İndirim Türü",
                RightLimitHeader = "Vergi", // Boş bırakıldığında otomatik olarak sayfa sonuna (Sağ taraf) kadar tarar
                RightLimitHeaderXOffset = 10,
                EndAnchor = "BU DÖNEME AİT İNDİRİLECEK KDV TUTARININ ORANLARA GÖRE DAĞILIMI",
                XOffset = -750,
                ManualWidth = null, // Otomatik hesaplanması için null bırakın
                Type = ExtractionType.TableColumnList
            };

            var settings = new MagickReadSettings { Density = new Density(300, 300) };

            using (var images = new MagickImageCollection())
            {
                images.Read(pdfPath, settings);

                // Sonuçları biriktirmek için değişkenlerimizi hazırlıyoruz
                decimal totalSum = 0;
                List<string> aggregatedList = new List<string>();
                bool anyDataFound = false;

                Console.WriteLine($"--- {rule.FieldName} İçin Tarama Başlatıldı ---");

                // TÜM SAYFALARI DÖNÜYORUZ
                for (int i = 0; i < images.Count; i++)
                {
                    Console.WriteLine($"Sayfa {i + 1} kontrol ediliyor...");

                    // Önemli: ProcessRule artık tablo bölünmüşse sayfa sonuna kadar tarıyor
                    var result = processor.ProcessRule(images[i], rule);

                    if (result != null)
                    {
                        anyDataFound = true;

                        if (result is decimal pageTotal)
                        {
                            // Sayısal toplama kuralı ise: Sayfa toplamını genel toplama ekle
                            totalSum += pageTotal;
                            if (pageTotal > 0)
                                Console.WriteLine($"> Sayfa {i + 1} üzerinde bulunan tutar: {pageTotal:N2} TL");
                        }
                        else if (result is List<string> pageList)
                        {
                            // Liste kuralı ise: Sayfadaki satırları ana listeye ekle
                            aggregatedList.AddRange(pageList);
                            Console.WriteLine($"> Sayfa {i + 1} üzerinde {pageList.Count} satır bulundu.");
                        }

                        // DİKKAT: Buradaki 'break;' kaldırıldı! 
                        // Böylece tablo diğer sayfalarda devam ediyorsa onları da yakalayacağız.
                    }
                }

                // --- TÜM SAYFALAR BİTTİKTEN SONRA SONUCU YAZDIR ---
                Console.WriteLine("\n" + new string('=', 40));

                if (anyDataFound)
                {
                    if (rule.Type == ExtractionType.TableColumnSum)
                    {
                        Console.WriteLine($"{rule.FieldName} GENEL TOPLAM: {totalSum:N2} TL");
                    }
                    else
                    {
                        Console.WriteLine($"{rule.FieldName} TOPLAM LİSTE:");
                        aggregatedList.ForEach(x => Console.WriteLine($"- {x}"));
                    }
                }
                else
                {
                    Console.WriteLine($"[UYARI] '{rule.FieldName}' tablosu veya çapa metinleri hiçbir sayfada bulunamadı.");
                }
                Console.WriteLine(new string('=', 40));
            }

            Console.ReadLine();
        }
    }
}