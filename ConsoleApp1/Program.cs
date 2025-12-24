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
    public enum ExtractionType { TableColumnList, TableColumnSum, SingleValue }

    public class OcrRule
    {
        public string FieldName { get; set; }
        public string AnchorText { get; set; }
        public ExtractionType Type { get; set; }
        public int XOffset { get; set; }
        public int YOffset { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            string tessData = @"C:\ocr";
            string pdfPath = @"D:\input.pdf";

            // Admin panelinden gelecek dinamik kural seti
            var rules = new List<OcrRule>
            {
                // Örnek 1 & 2: Tevkifat Uygulanmayanlar Vergi Listesi ve Toplamı
               new OcrRule {
                    FieldName = "Tevkifat Uygulanmayan Vergi",
                    AnchorText = "TEVKİFAT UYGULANMAYAN İŞLEMLER",
                    Type = ExtractionType.TableColumnSum,
                    XOffset = 1300, // 1800'den 1500'e çektik
                    YOffset = 50,  // Başlığın biraz daha altına (rakamların başladığı yer)
                    Width = 200,
                    Height = 150
                }
            };

            //var rules = new List<OcrRule>
            //{
            //    // Örnek 1 & 2: Tevkifat Uygulanmayanlar Vergi Listesi ve Toplamı
            //    new OcrRule {
            //        FieldName = "Tevkifat Uygulanmayan Vergi",
            //        AnchorText = "TEVKİFAT UYGULANMAYAN İŞLEMLER",
            //        Type = ExtractionType.TableColumnSum,
            //        XOffset = 1800, YOffset = 50, Width = 400, Height = 500
            //    },
            //    // Örnek 3: Kısmi Tevkifat İşlem Türü
            //    new OcrRule {
            //        FieldName = "İşlem Türü",
            //        AnchorText = "KISMİ TEVKİFAT UYGULANAN İŞLEMLER",
            //        Type = ExtractionType.TableColumnList,
            //        XOffset = -450, YOffset = 80, Width = 1100, Height = 400
            //    },
            //    // Örnek 4: İade Edilmesi Gereken KDV (Sayfa 2)
            //    new OcrRule {
            //        FieldName = "İade Edilmesi Gereken KDV",
            //        AnchorText = "DİĞER İADE HAKKI DOĞURAN İŞLEMLER",
            //        Type = ExtractionType.SingleValue,
            //        XOffset = 1800, YOffset = 450, Width = 400, Height = 100
            //    }
            //};

            ProcessYmmReport(pdfPath, tessData, rules);
            Console.ReadLine();
        }

        static void ProcessYmmReport(string pdfPath, string tessData, List<OcrRule> rules)
        {
            var settings = new MagickReadSettings { Density = new Density(300, 300) };
            using (var engine = new TesseractEngine(tessData, "tur", EngineMode.Default))
            using (var images = new MagickImageCollection())
            {
                images.Read(pdfPath, settings);

                foreach (var rule in rules)
                {
                    Console.WriteLine($"\n--- {rule.FieldName} İşleniyor ---");
                    int pageIndex = rule.AnchorText.Contains("İADE") ? 1 : 0;

                    if (pageIndex >= images.Count) continue;
                    var image = images[pageIndex];

                    var results = ExecuteRule(image, engine, rule);

                    // DEBUG: Toplam 0 olsa bile okunan her şeyi yazdır
                    Console.WriteLine("OCR tarafından okunan ham satırlar:");
                    results.ForEach(r => Console.WriteLine($"> '{r}'"));

                    if (rule.Type == ExtractionType.TableColumnSum)
                    {
                        decimal total = CalculateTotal(results);
                        Console.WriteLine($"Hesaplanan Toplam: {total:N2}");
                    }
                }
            }
        }

        static List<string> ExecuteRule(IMagickImage<byte> img, TesseractEngine engine, OcrRule rule)
        {
            Rect anchor = Rect.Empty;
            using (var pix = Pix.LoadFromMemory(img.ToByteArray(MagickFormat.Png)))
            using (var page = engine.Process(pix))
            {
                anchor = FindTextCoordinates(page, rule.AnchorText);
            }

            if (anchor != Rect.Empty)
            {
                // 1. Hedef koordinatları hesapla
                int tx = Math.Max(0, anchor.X1 + rule.XOffset);
                int ty = Math.Max(0, anchor.Y1 + rule.YOffset);

                // --- TEŞHİS MODU: Tam sayfa üzerinde işaretleme yap ---
                using (var diagnosticImg = img.Clone())
                {
                    var drawables = new Drawables()
                        .FillColor(MagickColors.Transparent)
                        .StrokeWidth(3)
                        // Çapayı KIRMIZI ile işaretle
                        .StrokeColor(MagickColors.Red)
                        .Rectangle(anchor.X1, anchor.Y1, anchor.X1 + anchor.Width, anchor.Y1 + anchor.Height)
                        // Kesilecek alanı MAVİ ile işaretle
                        .StrokeColor(MagickColors.Blue)
                        .Rectangle(tx, ty, tx + rule.Width, ty + rule.Height);

                    diagnosticImg.Draw(drawables);
                    diagnosticImg.Write($"debug_FULL_{rule.FieldName.Replace(" ", "_")}.png");
                    Console.WriteLine($"[TEŞHİS] Çerçeveli tam sayfa kaydedildi: debug_FULL_{rule.FieldName.Replace(" ", "_")}.png");
                }

                // 2. Kırpma işlemi
                using (var cropped = img.Clone())
                {
                    try
                    {
                        cropped.Crop(new MagickGeometry(tx, ty, (uint)rule.Width, (uint)rule.Height));
                        cropped.ResetPage();

                        string debugName = $"debug_CROP_{rule.FieldName.Replace(" ", "_")}.png";
                        cropped.Write(debugName);

                        using (var smallPix = Pix.LoadFromMemory(cropped.ToByteArray(MagickFormat.Png)))
                        using (var rPage = engine.Process(smallPix))
                        {
                            return rPage.GetText().Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[HATA] Kırpma sınır dışı kaldı: {ex.Message}");
                        return new List<string>();
                    }
                }
            }
            else
            {
                Console.WriteLine($"[HATA] Çapa bulunamadı: {rule.AnchorText}");
            }
            return new List<string>();
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

        private static Rect FindTextCoordinates(Page? page, string fullAnchorText)
        {
            var words = new List<(string Text, Rect Box)>();

            // 1. Sayfadaki tüm kelimeleri ve koordinatlarını bir listeye doldur
            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    string word = iter.GetText(PageIteratorLevel.Word);
                    if (!string.IsNullOrEmpty(word) && iter.TryGetBoundingBox(PageIteratorLevel.Word, out Rect rect))
                    {
                        words.Add((word.ToLower(), rect));
                    }
                } while (iter.Next(PageIteratorLevel.Word));
            }

            // 2. Kelime listesi içinde "fullAnchorText" cümlesini ara
            string searchTarget = fullAnchorText.ToLower();
            string[] targetParts = searchTarget.Split(' ');

            for (int i = 0; i <= words.Count - targetParts.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < targetParts.Length; j++)
                {
                    if (!words[i + j].Text.Contains(targetParts[j]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    // Eşleşme bulundu! Cümlenin başlangıç ve bitiş koordinatlarını birleştir
                    int x1 = words[i].Box.X1;
                    int y1 = words[i].Box.Y1;
                    int x2 = words[i + targetParts.Length - 1].Box.X2;
                    int y2 = words[i + targetParts.Length - 1].Box.Y2;

                    return new Rect(x1, y1, x2 - x1, y2 - y1);
                }
            }

            return Rect.Empty;
        }
    }
}