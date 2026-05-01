using System;

namespace samplevmptest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = "C# Hesap Makinesi";

            bool devamEt = true;

            Console.WriteLine("╔══════════════════════════════╗");
            Console.WriteLine("║       C# HESAP MAKİNESİ      ║");
            Console.WriteLine("╚══════════════════════════════╝");
            Console.WriteLine();

            while (devamEt)
            {
                double sayi1 = SayiAl("Birinci sayıyı girin: ");
                double sayi2 = SayiAl("İkinci sayıyı girin: ");

                Console.WriteLine();
                Console.WriteLine("İşlem seçin:");
                Console.WriteLine("  [1] Toplama     (+)");
                Console.WriteLine("  [2] Çıkarma     (-)");
                Console.WriteLine("  [3] Çarpma      (*)");
                Console.WriteLine("  [4] Bölme       (/)");
                Console.WriteLine("  [5] Mod         (%)");
                Console.WriteLine("  [6] Üs Alma     (^)");
                Console.WriteLine("  [7] Karekök     (√)");
                Console.Write("\nSeçiminiz: ");

                string secim = Console.ReadLine();
                Console.WriteLine();

                double sonuc;
                string islemAdi;

                switch (secim)
                {
                    case "1":
                        sonuc = sayi1 + sayi2;
                        islemAdi = "+";
                        Console.WriteLine($"  {sayi1} + {sayi2} = {sonuc}");
                        break;

                    case "2":
                        sonuc = sayi1 - sayi2;
                        islemAdi = "-";
                        Console.WriteLine($"  {sayi1} - {sayi2} = {sonuc}");
                        break;

                    case "3":
                        sonuc = sayi1 * sayi2;
                        islemAdi = "*";
                        Console.WriteLine($"  {sayi1} × {sayi2} = {sonuc}");
                        break;

                    case "4":
                        if (sayi2 == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("  HATA: Sıfıra bölme yapılamaz!");
                            Console.ResetColor();
                        }
                        else
                        {
                            sonuc = sayi1 / sayi2;
                            Console.WriteLine($"  {sayi1} ÷ {sayi2} = {sonuc}");
                        }
                        break;

                    case "5":
                        if (sayi2 == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("  HATA: Mod için bölen sıfır olamaz!");
                            Console.ResetColor();
                        }
                        else
                        {
                            sonuc = sayi1 % sayi2;
                            Console.WriteLine($"  {sayi1} % {sayi2} = {sonuc}");
                        }
                        break;

                    case "6":
                        sonuc = Math.Pow(sayi1, sayi2);
                        Console.WriteLine($"  {sayi1} ^ {sayi2} = {sonuc}");
                        break;

                    case "7":
                        Console.WriteLine("  Karekök işlemi için hangi sayı kullanılsın?");
                        Console.WriteLine($"  [1] {sayi1}   [2] {sayi2}");
                        Console.Write("  Seçiminiz: ");
                        string kareSecim = Console.ReadLine();
                        double kareHedef = kareSecim == "2" ? sayi2 : sayi1;

                        if (kareHedef < 0)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("  HATA: Negatif sayının karekökü alınamaz!");
                            Console.ResetColor();
                        }
                        else
                        {
                            sonuc = Math.Sqrt(kareHedef);
                            Console.WriteLine($"\n  √{kareHedef} = {sonuc}");
                        }
                        break;

                    default:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("  Geçersiz seçim! Lütfen 1-7 arasında bir değer girin.");
                        Console.ResetColor();
                        break;
                }

                Console.WriteLine();
                Console.WriteLine("─────────────────────────────");
                Console.Write("Yeni hesaplama yapmak ister misiniz? (E/H): ");
                string cevap = Console.ReadLine()?.Trim().ToUpper();
                devamEt = cevap == "E" || cevap == "EVET";
                Console.WriteLine();
            }

            Console.WriteLine("╔══════════════════════════════╗");
            Console.WriteLine("║   Hesap Makinesi Kapatıldı   ║");
            Console.WriteLine("╚══════════════════════════════╝");
            Console.WriteLine("Çıkmak için bir tuşa basın...");
            Console.ReadKey();
        }

        static double SayiAl(string mesaj)
        {
            double sayi;
            while (true)
            {
                Console.Write(mesaj);
                string girdi = Console.ReadLine();

                if (double.TryParse(girdi?.Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out sayi))
                {
                    return sayi;
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  Geçersiz giriş! Lütfen geçerli bir sayı girin.");
                Console.ResetColor();
            }
        }
    }
}
