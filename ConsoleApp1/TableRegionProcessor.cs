using ImageMagick;
using ImageMagick.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public List<string> ExtractDynamicTableData(IMagickImage<byte> fullImage, OcrRule rule)
        {
            // ÖNCE koordinatları tespit ediyoruz ve 'page' nesnesini hemen serbest bırakıyoruz
            Rect targetRect = Rect.Empty;
            Rect sectionStart = Rect.Empty;
            Rect sectionEnd = Rect.Empty;
            Rect colHeader = Rect.Empty;

            using (var pix = Pix.LoadFromMemory(fullImage.ToByteArray(MagickFormat.Png)))
            using (var page = _engine.Process(pix))
            {
                sectionStart = FindTextCoordinates(page, rule.StartAnchor);
                sectionEnd = FindTextCoordinates(page, rule.EndAnchor);

                if (sectionStart != Rect.Empty && sectionEnd != Rect.Empty)
                {
                    colHeader = FindTextWithConstraint(page, rule.ColumnHeader, sectionStart.Y1, sectionEnd.Y1);
                    var nextColHeader = FindTextWithConstraint(page, rule.RightLimitHeader, sectionStart.Y1, sectionEnd.Y1);

                    if (colHeader != Rect.Empty)
                    {
                        // Mavi Kutu Hesaplama Mantığı [cite: 35, 54]
                        int tx = colHeader.X1 + rule.XOffset;
                        int ty = colHeader.Y2 + 10;
                        int th = sectionEnd.Y1 - ty - 10;
                        int tw = rule.ManualWidth ?? (nextColHeader != Rect.Empty ? nextColHeader.X1 - tx - 10 : 800);

                        targetRect = new Rect(tx, ty, tw, th);
                    }
                }
            } // 'page' burada dispose edildi. Engine artık yeni bir process için hazır.

            if (targetRect == Rect.Empty)
            {
                Console.WriteLine("[HATA] Gerekli alanlar tespit edilemedi.");
                return new List<string>();
            }

            // 2. DEBUG ve OCR İşlemleri (Artık engine serbest)
            GenerateDebugImages(fullImage, rule.FieldName, sectionStart, sectionEnd, targetRect);

            return PerformCropAndOcr(fullImage, targetRect);
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
    }
}
