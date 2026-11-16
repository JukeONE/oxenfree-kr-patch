// skin2.py가 생성한 좌표 (skin.png 기준 픽셀)
using System.Drawing;
static class Skin {
  public const int Width = 548, Height = 942;
  public static readonly Rectangle Screen = new Rectangle(145, 390, 255, 367);
  public static readonly Rectangle[] Buttons = {
    new Rectangle(123, 830, 49, 50),
    new Rectangle(186, 830, 50, 50),
    new Rectangle(251, 830, 49, 50),
    new Rectangle(314, 830, 50, 50),
    new Rectangle(378, 830, 50, 50),
  };
  public static readonly Rectangle[] Faces = {
    new Rectangle(128, 835, 39, 40),
    new Rectangle(191, 835, 40, 40),
    new Rectangle(256, 835, 39, 40),
    new Rectangle(319, 835, 40, 40),
    new Rectangle(383, 835, 40, 40),
  };
  public static readonly Point[] Leds = {
    new Point(147, 809),
    new Point(211, 809),
    new Point(275, 809),
    new Point(339, 809),
    new Point(403, 809),
  };
}
