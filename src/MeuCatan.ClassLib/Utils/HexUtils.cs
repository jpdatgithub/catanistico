using MeuCatan.ClassLib.Contracts;

namespace MeuCatan.ClassLib.Utils;

public static class HexUtils
{
    public static (double X, double Y) CubeToCenterPixel(double centerx, double centery, int x, int y, int z, double raio)
    {
        double deslocamentoX = raio * (Math.Sqrt(3) * x + Math.Sqrt(3) / 2.0 * z);
        double deslocamentoY = raio * (3.0 / 2.0 * z);

        return (
            X: centerx + deslocamentoX,
            Y: centery + deslocamentoY
        );
    }

    public static List<Point> CalcularPontosSvg(double centerX, double centerY, double raio, double fator = 1.0)
    {
        var pontos = new List<Point>();

        for (int i = 0; i < 6; i++)
        {
            // Avança 60 graus no sentido horário (negativo no ciclo trigonométrico)
            double anguloGraus = -30 - (i * 60);
            double anguloRadianos = anguloGraus * (Math.PI / 180.0);

            // Cálculo com inversão de Y para SVG
            double x = centerX + raio * fator * Math.Cos(anguloRadianos);
            double y = centerY + raio * fator * Math.Sin(anguloRadianos);

            // Arredonda para 1 casa decimal
            x = Math.Round(x, 1);
            y = Math.Round(y, 1);

            pontos.Add(new Point(x, y));
        }

        return pontos;
    }
}