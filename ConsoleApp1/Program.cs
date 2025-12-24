using ImageMagick;
using Tesseract;
using System;
using System.Collections.Generic;
using System.IO;

namespace OcrProjesi
{
    public class OcrRule
    {
        public string FieldName { get; set; }
        public string AnchorText { get; set; }
        public int XOffset { get; set; }
        public int YOffset { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
            string tessDataPath = @"C:\ocr";
            string pdfPath = @"D:\input.pdf";

            // NOT: Önceki çıktınızda "Matrah" kelimesini okumuş. 
            // Bu, kutunun çok sağa veya yukarıda kaldığını gösterir.
            // Bu yüzden XOffset'i küçültüp YOffset'i (aşağı inmeyi) artırıyoruz.
            var rules = new List<OcrRule>
            {
                new OcrRule {
                    FieldName = "İşlem Türü",
                    AnchorText = "KISMİ",
                    // 'XOffset'i -450 yaparak metnin en soluna (sayfa başına) ulaşıyoruz.
                    XOffset = -650, 
                    // Mevcut görselde satır çok yukarıda kalmış, YOffset'i biraz daha artırarak 
                    // metni kutunun ortasına alıyoruz.
                    YOffset = 60,  
                    // Genişliği biraz artırıyoruz ki metin sağdan da kesilmesin ama 
                    // rakamlara çok girmesin.
                    Width = 2500,
                    Height = 450
                }
            };

            ProcessPdf(pdfPath, tessDataPath, rules);
        }

        static void ProcessPdf(string pdfPath, string tessData, List<OcrRule> rules)
        {
            var settings = new MagickReadSettings { Density = new Density(300, 300) };
            using (var engine = new TesseractEngine(tessData, "tur", EngineMode.Default))
            {
                using (var images = new MagickImageCollection())
                {
                    images.Read(pdfPath, settings);
                    for (int i = 0; i < images.Count; i++)
                    {
                        var currentImage = images[i];
                        foreach (var rule in rules)
                        {
                            string result = ExecuteRuleWithMagickCrop(currentImage, engine, rule, i + 1);
                            Console.WriteLine($"Sayfa {i + 1} - {rule.FieldName}: {result}");
                        }
                    }
                }
            }
        }

        static string ExecuteRuleWithMagickCrop(IMagickImage<byte> fullImage, TesseractEngine engine, OcrRule rule, int pageNum)
        {
            Rect anchorRect = Rect.Empty;

            // 1. ADIM: Koordinatları bulmak için tam resmi Tesseract'a ver
            using (var fullPix = Pix.LoadFromMemory(fullImage.ToByteArray(MagickFormat.Png)))
            {
                using (var page = engine.Process(fullPix))
                {
                    using (var iter = page.GetIterator())
                    {
                        iter.Begin();
                        do
                        {
                            string word = iter.GetText(PageIteratorLevel.Word);
                            if (!string.IsNullOrEmpty(word) && word.Contains(rule.AnchorText, StringComparison.OrdinalIgnoreCase))
                            {
                                iter.TryGetBoundingBox(PageIteratorLevel.Word, out anchorRect);
                                break;
                            }
                        } while (iter.Next(PageIteratorLevel.Word));
                    }
                }
            } // fullPix burada temizlenir, Tesseract motoru boşa çıkar.

            // 2. ADIM: Eğer çapa bulunduysa Magick.NET ile kırp ve tekrar oku
            if (anchorRect != Rect.Empty)
            {
                // Hedef bölgeyi hesapla
                // 1. ADIM: Hedef bölgeyi hesapla ve uint'e dönüştür
                // Koordinatlar negatif olamaz, Math.Max ile 0'dan küçük olmamasını sağlıyoruz
                int targetX = Math.Max(0, anchorRect.X1 + rule.XOffset);
                int targetY = Math.Max(0, anchorRect.Y1 + rule.YOffset);
                uint targetWidth = (uint)rule.Width;
                uint targetHeight = (uint)rule.Height;

                using (var croppedImage = fullImage.Clone())
                {
                    // 2. ADIM: MagickGeometry oluştururken uint cast kullanıyoruz
                    var geometry = new MagickGeometry(targetX, targetY, targetWidth, targetHeight);

                    // 3. ADIM: Kırpma işlemi
                    croppedImage.Crop(geometry);

                    // 4. ADIM: RePage yerine ResetPage kullanın veya Page özelliğini sıfırlayın
                    // Bu işlem kırpılan resmin koordinat sistemini (0,0) yapar.
                    croppedImage.ResetPage();

                    // DEBUG: Kırpılan bölgeyi kaydet
                    string debugFile = $"debug_p{pageNum}_{rule.FieldName.Replace(" ", "_")}.png";
                    croppedImage.Write(debugFile);

                    // Tesseract'a gönder...
                    using (var smallPix = Pix.LoadFromMemory(croppedImage.ToByteArray(MagickFormat.Png)))
                    {
                        using (var regionPage = engine.Process(smallPix))
                        {
                            return regionPage.GetText()?.Trim();
                        }
                    }
                }
            }

            return "[Çapa Bulunamadı]";
        }
    }
}