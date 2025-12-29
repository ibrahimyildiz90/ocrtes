using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tesseract;

namespace ConsoleApp1
{
    public class FieldRegionProcessor : TableRegionProcessor // Mevcut yardımcı metotları kullanmak için kalıtım alabilir
    {
        private readonly TesseractEngine _engine;
        public FieldRegionProcessor(string tessDataPath) : base(tessDataPath)
        {
            _engine = new TesseractEngine(tessDataPath, "tur", EngineMode.Default);
        }

        public object ProcessFieldRule(IMagickImage<byte> fullImage, OcrRule rule)
        {
            Rect targetRect = Rect.Empty;
            Rect sectionStart = Rect.Empty;
            Rect sectionEnd = Rect.Empty;
            Rect labelRect = Rect.Empty;

            using (var pix = Pix.LoadFromMemory(fullImage.ToByteArray(MagickFormat.Png)))
            using (var page = _engine.Process(pix))
            {
                // 1. Bölge sınırlarını bul (Örn: "DİĞER İADE HAKKI..." ve "DİĞER BİLGİLER")
                sectionStart = FindTextCoordinates(page, rule.StartAnchor);
                if (sectionStart == Rect.Empty) return null;

                sectionEnd = FindTextWithConstraint(page, rule.EndAnchor, sectionStart.Y1, 10000);
                if (sectionEnd == Rect.Empty || sectionEnd.Y1 <= sectionStart.Y1)
                {
                    // Bitiş çapası bulunamazsa makul bir mesafe aşağıyı hedefle (Örn: 500 pixel)
                    sectionEnd = new Rect(0, sectionStart.Y1 + 500, (int)fullImage.Width, 10);
                }

                // 2. Etiketi ara (Örn: "Sonraki Döneme Devreden...")
                labelRect = FindTextWithConstraint(page, rule.FieldName, sectionStart.Y1, sectionEnd.Y1);

                if (labelRect != Rect.Empty)
                {
                    // HESAPLAMA: Etiketin tam hizasındaki değeri bulmak için;
                    // X: Etiketin bittiği yerden ziyade, değerlerin sütun olarak hizalandığı sağ tarafa odaklanmalıyız.
                    int tx = labelRect.X2 + (rule.XOffset);

                    // Y: Etiketin tam satır hizası (Hafif bir dikey tolerans ile)
                    int ty = labelRect.Y1 - 10;
                    int th = labelRect.Height + 4;

                    int tw;
                    if (rule.ManualWidth.HasValue)
                        tw = rule.ManualWidth.Value;
                    else
                        tw = (int)fullImage.Width - tx - 20;

                    targetRect = new Rect(tx, ty, tw, th);
                }
            }

            if (targetRect == Rect.Empty) return null;

            // DEBUG: Mavi kutunun tam olarak sayının üzerine gelip gelmediğini kontrol edin
            GenerateDebugImages(fullImage, rule.FieldName, sectionStart, sectionEnd, targetRect);

            var lines = PerformCropAndOcr(fullImage, targetRect);

            // TEMİZLİK VE İŞLEME
            if (rule.Type == ExtractionType.TableColumnSum || rule.Type == ExtractionType.SingleValue)
            {
                decimal total = 0;
                bool foundAnyNumber = false;

                foreach (var line in lines)
                {
                    // OCR gürültüsünü (Z.USI gibi) temizlemek için içinde rakam olup olmadığını kontrol et
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\d"))
                    {
                        total += ParseTurkishNumber(line);
                        foundAnyNumber = true;
                    }
                }
                return foundAnyNumber ? (object)total : null;
            }

            return lines;
        }
    }
}
