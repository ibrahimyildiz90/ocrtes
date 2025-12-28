using ImageMagick;
using ImageMagick.Drawing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;
using YmmOcrSistemi;

namespace ConsoleApp1
{
    public class TableRegionProcessor
    {
        private readonly TesseractEngine _engine;

        public TableRegionProcessor(string tessDataPath)
        {
            _engine = new TesseractEngine(tessDataPath, "tur", EngineMode.Default);
        }

        public object ProcessRule(IMagickImage<byte> fullImage, OcrRule rule)
        {
            Rect targetRect = Rect.Empty;
            Rect sectionStart = Rect.Empty;
            Rect sectionEnd = Rect.Empty;
            Rect colHeader = Rect.Empty;

            using (var pix = Pix.LoadFromMemory(fullImage.ToByteArray(MagickFormat.Png)))
            using (var page = _engine.Process(pix))
            {
                // 1. Önce başlangıç çapasını tüm sayfada ara
                sectionStart = FindTextCoordinates(page, rule.StartAnchor);

                if (sectionStart == Rect.Empty) return null;

                // 2. KRİTİK DÜZELTME: Bitiş çapasını sadece başlangıç çapasının ALTINDA ara
                // sectionStart.Y1'den başlayarak sayfa sonuna (10000) kadar ara
                sectionEnd = FindTextWithConstraint(page, rule.EndAnchor, sectionStart.Y1, 10000);

                // 3. PARÇALI TABLO KONTROLÜ
                // Eğer bitiş çapası bulunamadıysa VEYA mantıksız bir yerdeyse sayfa sonuna kadar al
                if (sectionEnd == Rect.Empty || sectionEnd.Y1 <= sectionStart.Y1)
                {
                    int pageBottom = (int)fullImage.Height - 20;
                    sectionEnd = new Rect(0, pageBottom, (int)fullImage.Width, 10);
                    Console.WriteLine($"[BİLGİ] '{rule.FieldName}' için bitiş bulunamadı, sayfa sonu hedef alındı. (Y: {pageBottom})");
                }

                // 4. Kolon başlıklarını sectionStart ve sectionEnd arasında ara
                colHeader = FindTextWithConstraint(page, rule.ColumnHeader, sectionStart.Y1, sectionEnd.Y1);
                var nextColHeader = FindTextWithConstraint(page, rule.RightLimitHeader, sectionStart.Y1, sectionEnd.Y1);

                if (colHeader != Rect.Empty)
                {
                    int tx = colHeader.X1 + rule.XOffset;
                    int ty = colHeader.Y2 + 5;
                    int th = sectionEnd.Y1 - ty - 5;
                    int targetWidth;

                    if (rule.ManualWidth.HasValue)
                    {
                        targetWidth = rule.ManualWidth.Value;
                    }
                    else if (!string.IsNullOrEmpty(rule.RightLimitHeader) && nextColHeader != Rect.Empty)
                    {
                        // RightLimitHeaderXOffset geliştirmesini koruyoruz
                        targetWidth = nextColHeader.X1 - (rule.RightLimitHeaderXOffset ?? 0) - tx - 10;
                    }
                    else
                    {
                        targetWidth = (int)fullImage.Width - tx - 20;
                    }

                    targetRect = new Rect(tx, ty, targetWidth, th);
                }
            }

            if (targetRect == Rect.Empty)
            {
                Console.WriteLine($"[HATA] '{rule.FieldName}' için geçerli bir tarama alanı oluşturulamadı.");
                return null; // Program.cs'de birleştirebilmek için null dönüyoruz
            }

            // --- OCR ve Veri İşleme ---
            GenerateDebugImages(fullImage, rule.FieldName, sectionStart, sectionEnd, targetRect);
            var rawLines = PerformCropAndOcr(fullImage, targetRect);

            if (rule.Type == ExtractionType.TableColumnSum)
            {
                decimal total = 0;
                foreach (var line in rawLines) total += ParseTurkishNumber(line);
                return total;
            }

            return rawLines;
        }

        private List<string> PerformCropAndOcr(IMagickImage<byte> img, Rect region)
        {
            using (var cropped = img.Clone())
            {
                cropped.Crop(new MagickGeometry(region.X1, region.Y1, (uint)Math.Max(1, region.Width), (uint)Math.Max(1, region.Height)));
                cropped.ResetPage();

                using (var pix = Pix.LoadFromMemory(cropped.ToByteArray(MagickFormat.Png)))
                using (var page = _engine.Process(pix)) // Engine artık boşta olduğu için hata vermez
                {
                    return page.GetText().Split('\n')
                               .Select(s => s.Trim())
                               .Where(s => !string.IsNullOrEmpty(s))
                               .ToList();
                }
            }
        }

        private void GenerateDebugImages(IMagickImage<byte> img, string fieldName, Rect start, Rect end, Rect target)
        {
            string safeName = fieldName.Replace(" ", "_");

            using (var diagImg = img.Clone())
            {
                var drawables = new Drawables()
                    .FillColor(MagickColors.Transparent).StrokeWidth(4)
                    .StrokeColor(MagickColors.Red).Rectangle(start.X1, start.Y1, start.X2, start.Y2)
                    .StrokeColor(MagickColors.Green).Rectangle(end.X1, end.Y1, end.X2, end.Y2)
                    .StrokeColor(MagickColors.Blue).Rectangle(target.X1, target.Y1, target.X2, target.Y2);

                diagImg.Draw(drawables);
                diagImg.Write($"debug_FULL_{safeName}.png");
            }

            using (var cropImg = img.Clone())
            {
                cropImg.Crop(new MagickGeometry(target.X1, target.Y1, (uint)target.Width, (uint)target.Height));
                cropImg.ResetPage();
                cropImg.Write($"debug_CROP_{safeName}.png");
            }
        }

        private Rect FindTextCoordinates(Page page, string text) => FindTextWithConstraint(page, text, 0, 10000);

        private Rect FindTextWithConstraint(Page page, string fullText, int minY, int maxY)
        {
            // Eğer aranacak metin null veya boşsa, işlem yapmadan boş Rect dön.
            if (string.IsNullOrEmpty(fullText))
            {
                return Rect.Empty;
            }

            var words = new List<(string Text, Rect Box)>();
            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out Rect rect))
                    {
                        if (rect.Y1 >= minY && rect.Y1 <= maxY)
                        {
                            string word = iter.GetText(PageIteratorLevel.Word);
                            if (!string.IsNullOrEmpty(word)) words.Add((word.ToLower(), rect));
                        }
                    }
                } while (iter.Next(PageIteratorLevel.Word));
            }

            string[] targets = fullText.ToLower().Split(' ');
            for (int i = 0; i <= words.Count - targets.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < targets.Length; j++)
                    if (!words[i + j].Text.Contains(targets[j])) { match = false; break; }

                if (match)
                {
                    var first = words[i].Box;
                    var last = words[i + targets.Length - 1].Box;
                    return new Rect(first.X1, first.Y1, last.X2 - first.X1, last.Y2 - first.Y1);
                }
            }
            return Rect.Empty;
        }

        private decimal ParseTurkishNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            // 1. Regex ile sadece rakam, nokta ve virgül dışındaki her şeyi temizle
            string clean = Regex.Replace(text, @"[^0-9,\.]", "");

            if (string.IsNullOrEmpty(clean)) return 0;

            try
            {
                // 2. Format Dönüştürme:
                // Türkiye formatı: 1.250,50 
                // OCR bazen noktayı veya virgülü yanlış okuyabilir ama biz standartı takip ediyoruz:
                // Eğer hem nokta hem virgül varsa, nokta binlik ayıracıdır (sil), virgül ondalıktır (noktaya çevir).
                if (clean.Contains(",") && clean.Contains("."))
                {
                    clean = clean.Replace(".", ""); // Binlik ayıracı sil
                }

                // Virgülü nokta yap ki InvariantCulture ile decimal'e dönebilsin
                clean = clean.Replace(",", ".");

                if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
                {
                    return result;
                }
            }
            catch { /* Hatalı satırı pas geç */ }

            return 0;
        }
    }
}
