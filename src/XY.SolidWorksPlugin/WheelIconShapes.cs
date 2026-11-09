using System;
using System.Drawing;

namespace XY.SolidWorksPlugin;

// The extended half of the icon library: shapes 129..386 covering SOLIDWORKS
// modelling, sheet metal, weldments, stock profiles, machining and the process
// vocabulary the wheel works with. Shapes 0..128 live in WheelIconCatalog.
internal static partial class WheelIconCatalog
{
    private static void ShapeExtended(Graphics g, Pen b, Pen c, Pen a, int s)
    {
        switch (s)
        {
            // SW 文档与建模
            case 129: Cube(g,b);g.DrawEllipse(c,12,10,8,8);break;
            case 130: Cube(g,b);g.TranslateTransform(9,7);g.ScaleTransform(.55f,.55f);Cube(g,c);break;
            case 131: Page(g,b);Box(g,c,4,4,24,24);g.DrawLine(a,8,20,20,20);break;
            case 132: Box(g,b,3,3,26,26);for(int i=8;i<27;i+=6){g.DrawLine(c,i,3,i,29);g.DrawLine(c,3,i,29,i);}Arrow(g,a,6,26,26,6);break;
            case 133: Cube(g,b);Lines(g,c,3,20,29,20);Lines(g,a,3,25,29,25);break;
            case 134: Lines(g,b,22,6,10,6,10,14,22,14);Lines(g,c,10,10,22,10);Lines(g,a,8,20,24,20);break;
            case 135: Box(g,b,3,6,26,20);g.DrawLine(c,3,12,29,12);g.DrawLine(c,3,19,29,19);g.DrawLine(c,11,6,11,26);g.DrawLine(c,21,6,21,26);break;
            case 136: Cube(g,b);for(int i=0;i<4;i++)g.DrawLine(c,6+i*5,25-i*4,6+i*5,29-i*4);break;
            case 137: g.DrawEllipse(b,3,3,26,26);using(var f0=new SolidBrush(Color.FromArgb(120,b.Color)))g.FillPie(f0,3,3,26,26,180,180);break;
            case 138: g.DrawEllipse(b,10,4,12,12);g.DrawLine(c,12,16,12,20);g.DrawLine(c,14,16,14,20);g.DrawLine(c,16,22,16,26);for(int i=0;i<8;i++){double t=i*Math.PI/4;g.DrawLine(a,16+11*(float)Math.Cos(t),16+11*(float)Math.Sin(t),16+15*(float)Math.Cos(t),16+15*(float)Math.Sin(t));}break;
            case 139: Page(g,b);g.DrawLine(c,6,10,22,10);g.DrawLine(c,6,15,22,15);g.DrawLine(c,6,20,16,20);g.DrawEllipse(a,22,20,7,7);break;
            case 140: Poly(g,b,4,12,16,6,28,12,16,18);g.DrawLine(c,16,18,16,26);g.DrawLine(c,10,26,22,26);break;
            case 141: for(int i=4;i<29;i+=5)g.DrawLine(i%10==4?b:c,i,16,i+3,16);Arrow(g,a,4,16,29,16);break;
            case 142: Arrow(g,b,16,16,16,3);Arrow(g,c,16,16,29,16);Arrow(g,a,16,16,16,29);g.DrawEllipse(a,13,13,6,6);break;
            case 143: g.DrawEllipse(b,6,6,20,20);g.DrawLine(c,16,10,16,22);g.DrawLine(c,10,16,22,16);break;
            case 144: Poly(g,b,4,14,16,8,28,14,16,20);Arrow(g,c,16,20,16,29);g.DrawEllipse(a,13,13,6,6);break;
            // SW 特征建模
            case 145: Poly(g,b,4,10,12,6,20,10,12,14);Arrow(g,c,12,12,12,28);Box(g,a,8,26,8,4);break;
            case 146: g.DrawArc(b,4,4,24,24,270,180);g.DrawLine(c,16,4,16,28);Arrow(g,a,16,16,29,16);break;
            case 147: g.DrawBezier(b,4,26,12,4,20,28,28,6);g.DrawEllipse(c,3,3,6,6);g.DrawEllipse(a,24,4,6,6);break;
            case 148: g.DrawEllipse(b,3,6,10,8);g.DrawEllipse(c,3,18,10,8);Lines(g,a,13,10,19,12);Lines(g,a,13,22,19,20);Poly(g,a,19,8,29,12,29,20,19,24);break;
            case 149: g.DrawBezier(b,4,4,16,-2,24,10,28,6);g.DrawBezier(c,4,28,16,34,24,22,28,26);Lines(g,a,8,8,8,24,28,10,28,22);break;
            case 150: Lines(g,b,4,28,4,10,16,10,28,10,28,28);Lines(g,c,10,28,10,16,22,16,22,28);g.DrawLine(a,16,4,16,10);break;
            case 151: Box(g,b,4,4,24,24);Box(g,c,9,9,14,14);Lines(g,a,9,9,4,4);Lines(g,a,23,9,28,4);break;
            case 152: g.DrawArc(b,4,16,24,16,180,180);g.DrawLine(c,4,16,28,16);g.DrawEllipse(a,10,10,12,6);break;
            case 153: g.DrawEllipse(b,6,6,20,20);g.DrawEllipse(c,11,11,10,10);Arrow(g,a,16,3,16,11);break;
            case 154: g.DrawEllipse(b,5,5,22,22);g.DrawEllipse(c,10,10,12,12);g.DrawLine(a,16,3,16,10);break;
            case 155: g.DrawEllipse(b,6,4,20,8);for(int i=0;i<5;i++)g.DrawArc(c,6,4+i*4,20,8,0,180);break;
            case 156: g.DrawEllipse(b,4,6,24,20);for(int i=0;i<4;i++)g.DrawLine(c,6+i*5,6,6+i*5,26);Arrow(g,a,4,30,29,30);break;
            case 157: Lines(g,b,4,28,4,12,12,4,28,4);g.DrawLine(c,12,4,4,12);g.DrawLine(a,4,14,14,4);break;
            case 158: Lines(g,b,4,28,4,10,28,10,28,28);g.DrawArc(c,20,10,16,16,180,90);g.DrawArc(a,4,22,8,8,180,90);break;
            case 159: Lines(g,b,4,26,16,26,28,8);Lines(g,c,4,26,4,8);g.DrawLine(a,10,16,16,16);break;
            case 160: for(int y=0;y<3;y++)for(int x=0;x<3;x++)g.DrawEllipse((x+y)%2==0?b:c,4+x*9,4+y*9,6,6);break;
            // SW 切除与曲面
            case 161: g.DrawLine(c,16,2,16,30);Poly(g,b,4,10,13,6,13,26,4,22);Poly(g,a,28,10,19,6,19,26,28,22);break;
            case 162: Poly(g,b,4,10,12,6,20,10,12,14);Lines(g,c,12,16,12,26,10,30,14,30);g.DrawLine(a,8,18,16,18);break;
            case 163: g.DrawArc(b,4,4,24,24,270,180);for(int i=6;i<27;i+=5)g.DrawLine(c,i,16,i+2,16);break;
            case 164: g.DrawBezier(b,4,26,12,4,20,28,28,6);for(int i=0;i<4;i++)g.DrawLine(c,5+i*7,24,7+i*7,20);break;
            case 165: g.DrawEllipse(b,3,6,10,8);g.DrawEllipse(c,3,18,10,8);for(int i=0;i<3;i++)g.DrawLine(a,14+i*5,10,14+i*5,22);break;
            case 166: Box(g,b,4,4,24,24);for(int i=6;i<27;i+=5)g.DrawLine(c,i,4,i,28);g.DrawLine(a,16,2,16,30);break;
            case 167: Box(g,b,3,8,13,16);Box(g,c,16,8,13,16);Arrow(g,a,16,16,16,4);break;
            case 168: Poly(g,b,4,12,16,6,28,12,16,18);Arrow(g,c,16,20,16,29);Box(g,a,4,22,8,6);break;
            case 169: Poly(g,b,4,12,16,6,28,12,16,18);Lines(g,c,20,20,28,28);Lines(g,c,28,20,20,28);break;
            case 170: Poly(g,b,4,12,16,6,28,12,16,18);Arrow(g,c,20,22,28,14);Arrow(g,a,12,22,4,14);break;
            case 171: g.DrawBezier(b,4,10,14,4,20,16,28,10);g.DrawBezier(c,4,18,14,12,20,24,28,18);Lines(g,a,8,10,8,18);break;
            case 172: g.DrawBezier(b,4,20,14,10,20,22,28,14);Lines(g,c,4,26,28,26);Arrow(g,a,16,18,16,26);break;
            case 173: Poly(g,b,4,8,16,4,28,10,20,24,8,22);for(int y=8;y<24;y+=5)g.DrawLine(c,7,y,24,y);break;
            case 174: g.DrawBezier(b,4,12,14,6,20,18,28,12);g.DrawBezier(c,4,20,14,14,20,26,28,20);Lines(g,a,10,12,10,20);break;
            case 175: g.DrawBezier(b,4,14,14,8,20,20,24,14);Lines(g,c,24,14,29,14,27,12);Lines(g,c,24,14,27,16);break;
            case 176: g.DrawBezier(b,4,14,14,8,20,20,28,14);Lines(g,c,20,22,26,28);Lines(g,c,26,22,20,28);break;
            // 钣金特征
            case 177: Lines(g,b,4,12,14,8,14,24,4,20);Lines(g,c,28,12,18,8,18,24,28,20);Lines(g,a,14,16,18,16);break;
            case 178: Lines(g,b,3,24,3,10,23,10);Lines(g,c,23,10,29,16,29,24,3,24);g.DrawLine(a,3,17,23,17);break;
            case 179: Lines(g,b,3,24,3,12,20,12);Lines(g,c,20,12,20,4,29,4,29,20,3,20);break;
            case 180: Lines(g,b,4,26,4,14,14,14,14,6,28,6);Lines(g,c,14,14,26,26);g.DrawLine(a,18,10,24,16);break;
            case 181: Lines(g,b,4,26,4,8,14,8);g.DrawArc(c,14,8,12,12,180,270);g.DrawLine(a,14,20,8,20);break;
            case 182: Lines(g,b,3,24,3,12,11,12,11,7,22,7,22,12,29,12);Lines(g,c,3,20,11,20);Lines(g,a,22,20,29,20);break;
            case 183: Lines(g,b,4,8,14,8,14,20);Lines(g,c,14,20,28,20);Arrow(g,a,20,12,28,14);break;
            case 184: Lines(g,c,28,8,18,8,18,20);Lines(g,b,18,20,4,20);Arrow(g,a,24,16,16,14);break;
            case 185: Lines(g,b,3,24,3,14,10,14);g.DrawArc(c,10,10,12,12,90,180);Lines(g,c,22,16,29,16);g.DrawLine(a,18,6,18,12);break;
            case 186: Lines(g,b,4,26,4,10,20,10,20,26);Lines(g,c,20,26,28,26,28,10);g.DrawLine(a,20,10,28,10);break;
            case 187: Lines(g,b,4,26,4,10,20,10);Lines(g,c,20,10,28,18,28,26);Lines(g,a,4,18,12,18);break;
            case 188: Lines(g,b,4,26,4,10,20,10,20,26);Poly(g,c,20,10,28,18,20,26);for(int i=0;i<3;i++)g.DrawLine(a,22+i*2,14+i*2,24+i*2,16+i*2);break;
            case 189: Box(g,b,4,6,24,20);for(int y=9;y<25;y+=4)g.DrawLine(c,6,y,26,y);break;
            case 190: Box(g,b,4,6,24,20);for(int y=9;y<25;y+=4){g.DrawLine(c,6,y,26,y-2);g.DrawLine(a,6,y+1,26,y-1);}break;
            case 191: Lines(g,b,4,26,4,8,16,8,16,26);Lines(g,c,16,8,28,8,28,26);Lines(g,a,16,18,28,18);break;
            case 192: Poly(g,b,4,26,4,10,24,26);Lines(g,c,10,26,10,18,18,26);g.DrawLine(a,14,14,22,22);break;
            // 焊件与型材
            case 193: Lines(g,b,4,12,14,12,14,24,26,24);g.DrawArc(c,14,12,10,10,180,90);g.DrawArc(a,16,14,10,10,0,90);break;
            case 194: Box(g,b,4,4,24,24);g.DrawLine(c,4,12,28,12);g.DrawLine(c,4,20,28,20);g.DrawLine(c,12,4,12,28);g.DrawLine(c,20,4,20,28);break;
            case 195: Lines(g,b,4,8,28,8,28,24,4,24);g.DrawLine(c,4,12,28,12);g.DrawLine(c,4,20,28,20);g.DrawEllipse(a,10,12,5,8);break;
            case 196: Lines(g,b,4,8,28,8,28,24,4,24);Lines(g,c,20,4,20,28);Lines(g,a,20,6,26,10);break;
            case 197: Poly(g,b,5,26,5,12,25,26);g.DrawLine(c,12,26,12,19);g.DrawLine(a,18,26,18,22);break;
            case 198: Box(g,b,4,10,24,14);Box(g,c,8,6,16,4);g.DrawLine(a,4,17,28,17);break;
            case 199: Lines(g,b,4,22,28,22);for(int i=0;i<5;i++)g.DrawArc(c,4+i*5,16,7,7,180,180);break;
            case 200: Lines(g,b,4,26,4,14,16,14);Lines(g,c,16,14,28,26);for(int i=0;i<4;i++)g.DrawLine(a,8+i*4,20-i*2,10+i*4,22-i*2);break;
            case 201: Lines(g,b,4,20,14,20);Lines(g,c,14,12,14,28);Poly(g,a,18,12,26,12,22,20);g.DrawLine(b,4,20,10,20);break;
            case 202: Box(g,b,6,6,20,20);Box(g,c,11,11,10,10);break;
            case 203: Box(g,b,3,9,26,14);Box(g,c,8,13,16,6);break;
            case 204: g.DrawEllipse(b,4,4,24,24);g.DrawEllipse(c,10,10,12,12);break;
            case 205: Lines(g,b,4,26,4,8,14,8,14,16,22,16);Lines(g,c,4,20,10,20);g.DrawLine(a,14,8,22,8);break;
            case 206: Lines(g,b,6,6,6,26,18,26,18,6);Lines(g,c,6,10,14,10);Lines(g,c,6,22,14,22);break;
            case 207: Lines(g,b,6,4,6,28);Lines(g,c,26,4,26,28);g.DrawLine(b,6,16,26,16);g.DrawLine(a,10,4,10,28);break;
            case 208: Box(g,b,3,12,26,8);g.DrawLine(c,3,16,29,16);break;
            // 下料与机加工
            case 209: g.DrawEllipse(b,4,4,24,24);g.DrawEllipse(c,11,11,10,10);break;
            case 210: Lines(g,b,4,24,28,24,28,12,4,12);Arrow(g,c,16,2,16,12);for(int i=0;i<5;i++)g.DrawLine(a,10+i*3,14+i*2,12+i*3,20+i*1);break;
            case 211: Box(g,b,4,18,24,8);Box(g,c,11,4,10,10);Arrow(g,a,16,14,16,18);break;
            case 212: Lines(g,b,4,20,28,20);for(int i=4;i<28;i+=3)g.DrawLine(c,i,20,i,15);Arrow(g,a,4,10,28,10);break;
            case 213: Lines(g,b,4,26,4,10,20,10);g.DrawLine(c,4,10,10,16);g.DrawLine(a,4,14,8,18);break;
            case 214: Lines(g,b,13,3,13,16,9,26,17,26,13,16);for(int i=0;i<3;i++)g.DrawLine(c,9+i*0+2,20+i*2,15,18+i*2);Arrow(g,a,4,26,9,26);break;
            case 215: Box(g,b,12,3,8,12);Lines(g,c,12,15,10,27,18,27,16,15);for(int i=0;i<4;i++)g.DrawLine(a,10,18+i*3,18,17+i*3);break;
            case 216: Lines(g,b,3,12,10,12,16,26,22,12,29,12);g.DrawLine(c,10,12,22,12);break;
            case 217: Lines(g,b,3,14,10,14,10,22,3,22);Lines(g,c,22,14,29,14,29,22,22,22);g.DrawLine(a,10,18,22,18);break;
            case 218: g.DrawEllipse(b,8,4,16,16);for(int i=0;i<6;i++){double t=i*Math.PI/3;g.DrawLine(c,16+8*(float)Math.Cos(t),12+8*(float)Math.Sin(t),16+12*(float)Math.Cos(t),12+12*(float)Math.Sin(t));}Lines(g,a,14,20,14,29,18,29,18,20);break;
            case 219: Box(g,b,4,14,18,12);Poly(g,c,22,20,29,14,29,26,24,22);Arrow(g,a,4,20,22,20);break;
            case 220: g.DrawEllipse(b,4,8,20,20);g.DrawEllipse(c,11,15,6,6);Lines(g,a,24,10,29,16,24,22);break;
            case 221: Box(g,b,4,6,24,20);for(int x=8;x<26;x+=8)g.DrawLine(c,x,4,x,28);g.DrawEllipse(a,14,14,4,4);break;
            case 222: Lines(g,b,6,26,6,14,26,14,26,26);for(int i=0;i<5;i++)g.DrawLine(c,9+i*4,10,11+i*4,4);g.DrawEllipse(a,14,20,4,4);break;
            case 223: Lines(g,b,4,24,28,24);g.DrawArc(c,6,6,20,18,200,140);Star(g,a,16,10,3.5f);break;
            case 224: Box(g,b,4,6,24,20);for(int y=9;y<25;y+=3)g.DrawLine(c,6,y,26,y);break;
            // 焊接与检验
            case 225: Box(g,b,4,6,24,20);for(int i=0;i<10;i++)g.DrawEllipse(c,6+(i%5)*5,9+(i/5)*6,2,2);Arrow(g,a,16,2,16,6);break;
            case 226: Lines(g,b,3,12,14,12);Lines(g,c,18,12,29,12);Lines(g,b,3,20,14,20);Lines(g,c,18,20,29,20);for(int i=0;i<3;i++)g.DrawEllipse(a,15,11+i*4,2,2);break;
            case 227: Lines(g,b,3,16,14,16);Lines(g,c,18,16,29,16);g.DrawEllipse(a,14,14,4,4);break;
            case 228: Lines(g,b,3,16,29,16);for(int i=0;i<7;i++)g.DrawArc(c,3+i*4,13,5,6,180,180);break;
            case 229: Lines(g,b,3,16,29,16);for(int i=0;i<3;i++)g.DrawArc(c,4+i*10,13,6,6,180,180);break;
            case 230: Box(g,b,4,10,24,14);for(int i=0;i<8;i++)g.DrawLine(c,6+i*3,12,6+i*3,22);Arrow(g,a,4,6,28,6);break;
            case 231: Lines(g,b,4,20,16,20,28,12);Arrow(g,c,16,20,22,20);Lines(g,a,4,26,28,26);break;
            case 232: Box(g,b,3,6,12,20);Box(g,c,17,6,12,20);Arrow(g,a,15,16,15,16);g.DrawLine(a,15,10,15,22);break;
            case 233: Box(g,b,3,6,12,20);Box(g,c,17,6,12,20);Lines(g,a,13,10,19,22);Lines(g,a,19,10,13,22);break;
            case 234: g.DrawEllipse(b,6,6,14,14);for(int i=0;i<8;i++){double t=i*Math.PI/4;g.DrawLine(c,13+7*(float)Math.Cos(t),13+7*(float)Math.Sin(t),13+10*(float)Math.Cos(t),13+10*(float)Math.Sin(t));}Lines(g,a,22,24,29,17,26,14,25,20,22,18);break;
            case 235: g.DrawEllipse(b,4,4,15,15);g.DrawLine(c,15,15,27,27);Box(g,a,19,19,9,9);break;
            case 236: Lines(g,b,3,10,3,22,9,22,9,10);Lines(g,c,3,16,9,16);Arrow(g,a,9,22,29,22);Arrow(g,a,29,10,9,10);break;
            case 237: Box(g,b,4,4,24,24);Lines(g,c,16,4,16,28);Lines(g,c,4,16,28,16);g.DrawEllipse(a,13,13,6,6);Arrow(g,b,16,16,16,2);break;
            case 238: Box(g,b,5,8,22,18);Lines(g,c,10,12,10,22);Lines(g,a,6,14,14,14);Star(g,a,25,10,3.5f);break;
            case 239: Box(g,b,4,6,24,20);Arrow(g,c,8,16,24,16);g.DrawEllipse(a,12,16,3,3);g.DrawEllipse(a,20,16,3,3);break;
            case 240: Box(g,b,4,6,24,20);Lines(g,c,9,16,14,21,23,11);break;
            // 表面与热处理
            case 241: Page(g,b);g.DrawEllipse(c,20,18,9,9);Lines(g,a,22,22,24,24,28,20);break;
            case 242: Box(g,b,4,10,24,16);Lines(g,c,4,10,28,10);for(int i=0;i<4;i++)g.DrawLine(a,7+i*6,10,7+i*6,26);break;
            case 243: Box(g,b,4,16,16,12);Lines(g,c,20,16,24,10,28,16);g.DrawLine(c,24,10,24,6);for(int i=0;i<3;i++)g.DrawLine(a,26+i*2,18+i*3,28+i*2,20+i*3);break;
            case 244: Box(g,b,4,16,16,12);Arrow(g,c,20,14,28,6);for(int i=0;i<6;i++)g.DrawEllipse(a,10+(i%3)*6,4+(i/3)*5,2,2);break;
            case 245: Box(g,b,3,18,14,10);Lines(g,c,17,18,21,10,25,18);for(int i=0;i<5;i++)g.DrawLine(a,22,6,20+i*2,2);break;
            case 246: Box(g,b,3,6,26,20);Lines(g,c,10,6,10,26);Lines(g,a,22,6,22,26);g.DrawLine(b,10,16,22,16);g.DrawLine(a,6,16,6,20);g.DrawLine(a,26,16,26,20);break;
            case 247: Box(g,b,4,10,24,16);for(int i=0;i<5;i++)g.DrawLine(c,6+i*5,10,6+i*5,26);Lines(g,a,4,6,28,6);break;
            case 248: Box(g,b,4,10,24,16);Lines(g,c,10,10,10,26);Lines(g,a,22,10,22,26);g.DrawEllipse(c,13,14,6,8);break;
            case 249: Box(g,b,4,10,24,16);using(var f1=new SolidBrush(Color.FromArgb(150,40,40,40)))g.FillRectangle(f1,4,10,24,16);g.DrawRectangle(c,4,10,24,16);break;
            case 250: Box(g,b,4,10,24,16);for(int i=0;i<6;i++)g.DrawEllipse(c,6+i*4,14,2,2);g.DrawLine(a,4,22,28,22);break;
            case 251: Box(g,b,4,10,24,16);g.DrawArc(c,4,10,24,16,180,180);g.DrawLine(a,4,18,28,18);break;
            case 252: Lines(g,b,16,28,10,20,13,12,16,16,19,8,22,14,20,20,16,28);g.DrawArc(c,8,6,16,12,200,140);break;
            case 253: Lines(g,b,16,28,10,20,13,12,16,16,19,8,22,14,20,20,16,28);Box(g,c,4,22,24,6);Arrow(g,a,16,20,16,26);break;
            case 254: Lines(g,b,16,28,10,20,13,12,16,16,19,8,22,14,20,20,16,28);g.DrawArc(c,6,4,20,14,180,180);g.DrawLine(a,4,26,28,26);break;
            case 255: Box(g,b,4,8,24,18);for(int i=0;i<8;i++)g.DrawLine(c,7+i*3,8,7+i*3,12);Arrow(g,a,16,26,16,16);break;
            case 256: Box(g,b,4,8,24,18);for(int i=0;i<8;i++)g.DrawEllipse(c,7+i*3,14,2,2);Arrow(g,a,16,26,16,20);break;
            // 装配与紧固
            case 257: Box(g,b,4,8,24,18);g.DrawEllipse(c,22,4,8,8);Lines(g,a,26,6,26,8,28,8);break;
            case 258: Box(g,b,3,10,12,14);Box(g,c,17,10,12,14);Lines(g,a,15,14,17,14);Lines(g,a,15,20,17,20);break;
            case 259: g.DrawEllipse(b,4,4,10,10);g.DrawEllipse(c,18,18,10,10);Lines(g,a,14,14,18,18);g.DrawLine(a,10,20,22,8);break;
            case 260: Box(g,b,3,6,10,9);Box(g,c,19,17,10,9);Box(g,a,3,17,10,9);for(int i=0;i<3;i++)g.DrawLine(a,8,15-i*2,8,17-i*2);break;
            case 261: Box(g,b,3,6,10,9);Box(g,c,19,17,10,9);for(int i=0;i<4;i++)g.DrawLine(a,13,15-i*3,19,9+i*1);break;
            case 262: g.DrawEllipse(b,8,8,16,16);g.DrawEllipse(c,14,14,16,16);using(var f2=new SolidBrush(Color.FromArgb(90,200,40,40)))g.FillEllipse(f2,14,14,16,16);break;
            case 263: g.DrawEllipse(b,4,10,10,10);g.DrawEllipse(c,18,10,10,10);g.DrawLine(a,9,15,23,15);Arrow(g,a,16,28,16,20);break;
            case 264: g.DrawEllipse(b,4,10,12,12);Poly(g,c,20,8,28,12,28,20,20,24);g.DrawLine(a,12,16,20,16);break;
            case 265: Box(g,b,4,14,24,10);Box(g,c,11,4,10,8);g.DrawLine(a,16,12,16,24);g.DrawLine(c,10,8,22,8);break;
            case 266: Box(g,b,11,4,10,8);Lines(g,c,12,12,12,28,20,28,20,12);for(int i=0;i<4;i++)g.DrawLine(a,12,16+i*3,20,15+i*3);break;
            case 267: g.DrawEllipse(b,5,5,22,22);g.DrawEllipse(c,12,12,8,8);break;
            case 268: g.DrawArc(b,5,5,22,22,0,300);g.DrawLine(c,26,16,20,10);g.DrawLine(a,26,16,28,20);break;
            case 269: g.DrawArc(b,5,5,22,22,40,280);g.DrawEllipse(c,12,12,3,3);g.DrawEllipse(a,19,19,3,3);break;
            case 270: Box(g,b,12,4,8,12);Lines(g,c,12,16,10,28,18,28,16,16);g.DrawLine(a,10,24,18,24);break;
            case 271: g.DrawEllipse(b,10,3,12,7);Lines(g,c,11,10,11,26,21,26,21,10);g.DrawLine(a,11,18,21,18);break;
            case 272: g.DrawEllipse(b,9,3,14,7);Lines(g,c,12,10,12,20,20,20,20,10);Arrow(g,a,16,20,16,29);break;
            // 传动与气动
            case 273: Box(g,b,4,12,24,12);g.DrawEllipse(c,10,12,12,12);for(int i=0;i<5;i++)g.DrawLine(a,7+i*5,12,7+i*5,18);break;
            case 274: Box(g,b,4,12,24,12);g.DrawEllipse(c,10,12,12,12);for(int i=0;i<4;i++)g.DrawArc(a,7+i*5,10,5,5,180,180);break;
            case 275: Box(g,b,4,10,24,8);Lines(g,c,12,18,12,28,20,28,20,18);Arrow(g,a,16,4,16,10);break;
            case 276: Box(g,b,4,14,24,8);Lines(g,c,12,4,12,14,20,14,20,4);for(int i=0;i<3;i++)g.DrawLine(a,12,8+i*3,20,7+i*3);break;
            case 277: Poly(g,b,8,6,24,6,28,14,24,22,8,22,4,14);g.DrawEllipse(c,12,10,8,8);for(int i=0;i<3;i++)g.DrawLine(a,4,10+i*3,28,10+i*3);break;
            case 278: g.DrawEllipse(b,3,3,26,26);g.DrawEllipse(c,9,9,14,14);for(int i=0;i<8;i++){double t=i*Math.PI/4;g.DrawEllipse(a,16+8*(float)Math.Cos(t)-1.5f,16+8*(float)Math.Sin(t)-1.5f,3,3);}break;
            case 279: for(int i=0;i<5;i++)g.DrawEllipse(i%2==0?b:c,5,5+i*5,22,7);break;
            case 280: g.DrawEllipse(b,4,4,24,24);g.DrawEllipse(c,10,10,12,12);g.DrawLine(a,4,4,10,10);break;
            case 281: for(int i=0;i<3;i++){g.DrawEllipse(b,4+i*9,10,9,12);g.DrawEllipse(c,8+i*9,10,9,12);}break;
            case 282: g.DrawEllipse(b,4,8,10,16);g.DrawEllipse(c,18,8,10,16);Lines(g,a,14,8,18,8);Lines(g,a,14,24,18,24);break;
            case 283: Box(g,b,4,10,10,12);Box(g,c,18,10,10,12);g.DrawLine(a,14,16,18,16);g.DrawEllipse(b,9,16,4,4);break;
            case 284: Box(g,b,6,8,20,16);Lines(g,c,6,12,26,12);Lines(g,c,6,20,26,20);Arrow(g,a,26,16,29,16);g.DrawEllipse(b,12,13,8,8);break;
            case 285: g.DrawEllipse(b,5,8,13,13);g.DrawEllipse(c,14,4,14,14);Box(g,a,4,22,24,6);g.DrawLine(a,10,22,10,28);break;
            case 286: Box(g,b,4,8,24,16);Lines(g,c,4,16,28,16);Arrow(g,a,16,16,16,28);g.DrawEllipse(b,9,11,5,5);break;
            case 287: Box(g,b,4,10,24,12);Box(g,c,10,4,12,6);g.DrawLine(a,10,10,22,10);Arrow(g,a,16,22,16,29);break;
            case 288: Box(g,b,4,6,24,20);Lines(g,c,4,16,28,16);Arrow(g,a,16,16,16,29);Box(g,b,9,9,5,5);break;
            // 电气与控制
            case 289: g.DrawEllipse(b,5,8,18,18);Lines(g,c,23,16,29,16,29,10);Arrow(g,a,14,26,14,29);break;
            case 290: Box(g,b,3,12,26,8);for(int i=0;i<5;i++)g.DrawEllipse(c,6+i*5,14,3,3);Box(g,a,10,6,12,6);break;
            case 291: Box(g,b,3,12,26,8);Box(g,c,10,8,12,6);Arrow(g,a,16,22,26,22);break;
            case 292: Box(g,b,3,14,26,6);for(int i=0;i<7;i++)g.DrawLine(c,5+i*4,14,7+i*4,20);Box(g,a,4,8,8,6);break;
            case 293: Box(g,b,4,6,24,20);Lines(g,c,8,12,8,20);Lines(g,c,12,12,12,20);Lines(g,a,18,12,24,12);Lines(g,a,18,20,24,20);break;
            case 294: Box(g,b,4,6,24,20);g.DrawBezier(c,7,22,11,10,14,26,18,12);Arrow(g,a,21,22,26,10);break;
            case 295: Box(g,b,4,6,24,20);for(int y=10;y<24;y+=5)g.DrawLine(c,7,y,25,y);g.DrawEllipse(a,9,16,2,2);g.DrawEllipse(a,22,16,2,2);break;
            case 296: Box(g,b,4,4,24,24);Box(g,c,8,8,16,16);g.DrawLine(a,12,20,20,12);g.DrawLine(a,20,20,12,12);break;
            case 297: g.DrawEllipse(b,6,6,14,14);g.DrawArc(c,4,10,10,10,300,120);g.DrawArc(a,18,10,10,10,240,120);g.DrawLine(b,2,16,6,16);g.DrawLine(b,26,16,30,16);break;
            case 298: Box(g,b,4,10,12,14);Arrow(g,c,16,17,28,17);Arrow(g,a,26,12,16,12);g.DrawEllipse(a,10,17,3,3);break;
            case 299: Box(g,b,4,8,24,16);Lines(g,c,10,8,10,24);Arrow(g,a,10,16,22,10);g.DrawEllipse(b,20,13,6,6);break;
            case 300: Box(g,b,4,8,24,16);Lines(g,c,10,12,18,12);Lines(g,c,10,20,18,20);Arrow(g,a,18,16,26,16);g.DrawLine(a,10,16,14,16);break;
            case 301: Box(g,b,4,8,24,16);Lines(g,c,10,16,16,16);Lines(g,a,16,16,20,10,26,16);g.DrawEllipse(b,8,16,3,3);break;
            case 302: Box(g,b,4,6,24,20);Lines(g,c,10,10,10,22);Lines(g,a,18,10,18,22);g.DrawLine(b,10,16,18,16);g.DrawLine(a,14,12,14,20);break;
            case 303: g.DrawEllipse(b,4,8,12,16);g.DrawEllipse(c,18,8,12,16);Lines(g,a,16,6,16,26);break;
            case 304: Box(g,b,4,10,24,12);for(int i=0;i<3;i++){g.DrawEllipse(c,7+i*7,16,4,4);g.DrawLine(a,9+i*7,10,9+i*7,16);}break;
            // 五金与门柜
            case 305: g.DrawBezier(b,3,20,12,8,20,26,29,14);g.DrawEllipse(c,2,18,5,5);g.DrawEllipse(a,26,12,5,5);break;
            case 306: Box(g,b,4,6,10,20);Box(g,c,18,6,10,20);g.DrawEllipse(a,14,12,4,4);g.DrawEllipse(a,14,20,4,4);g.DrawLine(b,14,6,14,26);break;
            case 307: Box(g,b,6,14,20,14);g.DrawArc(c,10,6,12,12,180,180);g.DrawEllipse(a,14,19,4,4);g.DrawLine(a,16,19,16,23);break;
            case 308: Box(g,b,4,10,10,14);g.DrawArc(c,14,8,14,18,270,180);g.DrawLine(a,4,17,14,17);break;
            case 309: Box(g,b,3,6,8,20);g.DrawArc(c,11,10,18,14,270,180);g.DrawLine(a,3,16,11,16);break;
            case 310: Box(g,b,10,4,12,24);g.DrawLine(c,10,10,22,10);g.DrawLine(c,10,22,22,22);break;
            case 311: Lines(g,b,5,24,5,14,27,14,27,24);g.DrawArc(c,8,24,18,12,180,180);g.DrawLine(a,5,19,27,19);break;
            case 312: Lines(g,b,6,10,6,20);Box(g,c,10,8,12,6);Lines(g,a,12,14,12,26,20,26,20,14);Box(g,b,8,26,16,4);break;
            case 313: Lines(g,b,4,28,10,28,10,10,22,10,22,28,28,28);Lines(g,c,4,28,28,28);for(int i=0;i<3;i++)g.DrawLine(a,12+i*4,14,12+i*4,20);break;
            case 314: Lines(g,b,7,10,7,20,25,20,25,10);g.DrawArc(c,7,20,18,14,180,180);g.DrawLine(a,7,15,25,15);for(int i=0;i<4;i++)g.DrawLine(c,10+i*4,10,10+i*4,6);break;
            case 315: g.DrawEllipse(b,6,8,14,14);Box(g,c,11,3,10,6);g.DrawLine(a,6,22,20,22);g.DrawEllipse(a,12,8,3,3);break;
            case 316: g.DrawEllipse(b,6,10,14,14);Box(g,c,10,4,12,7);Arrow(g,a,16,4,26,4);g.DrawLine(a,6,24,20,24);break;
            case 317: Lines(g,b,4,24,4,16,28,16,28,24);for(int i=0;i<4;i++)g.DrawArc(c,5+i*6,10,6,6,180,180);g.DrawLine(a,4,28,28,28);break;
            case 318: Lines(g,b,4,22,4,12,28,12,28,22);for(int i=0;i<7;i++)g.DrawLine(c,6+i*3,16,7+i*3,19);g.DrawLine(a,4,26,28,26);break;
            case 319: Lines(g,b,6,28,6,10,26,10,26,28);Lines(g,c,6,18,26,18);Poly(g,a,10,10,22,10,16,4);break;
            case 320: g.DrawEllipse(b,5,9,14,14);Box(g,c,10,3,10,6);g.DrawEllipse(a,19,9,8,8);g.DrawLine(a,6,23,19,23);break;
            // 流体与管件
            case 321: g.DrawEllipse(b,5,9,14,14);g.DrawEllipse(c,10,14,4,4);Box(g,a,10,3,10,6);g.DrawLine(a,19,16,26,16);break;
            case 322: g.DrawEllipse(b,5,5,22,22);for(int i=0;i<4;i++){double t=i*Math.PI/2+.6;g.DrawBezier(c,16,16,16+10*(float)Math.Cos(t),16+10*(float)Math.Sin(t),16+12*(float)Math.Cos(t+1),16+12*(float)Math.Sin(t+1),16+3*(float)Math.Cos(t+2),16+3*(float)Math.Sin(t+2));}g.DrawEllipse(a,14,14,4,4);break;
            case 323: Box(g,b,4,8,24,16);for(int i=0;i<4;i++)g.DrawLine(c,7+i*6,8,7+i*6,24);Arrow(g,a,16,2,16,8);Arrow(g,a,16,30,16,24);break;
            case 324: Box(g,b,4,10,24,6);for(int i=0;i<5;i++){g.DrawLine(c,7+i*5,16,7+i*5,26);g.DrawLine(a,7+i*5,4,7+i*5,10);}break;
            case 325: g.DrawEllipse(b,5,8,18,18);Box(g,c,23,12,7,10);Arrow(g,a,16,4,16,8);g.DrawEllipse(a,12,15,7,7);break;
            case 326: g.DrawEllipse(b,5,8,18,18);Box(g,c,23,12,7,10);Lines(g,a,12,12,12,20,18,20,18,12);break;
            case 327: g.DrawEllipse(b,8,10,16,16);g.DrawLine(c,16,10,16,2);Box(g,a,10,2,12,5);g.DrawLine(b,8,18,24,18);break;
            case 328: Box(g,b,4,12,12,8);Box(g,c,16,12,12,8);g.DrawLine(a,16,16,16,16);g.DrawLine(b,16,16,16,16);g.DrawLine(a,12,16,20,16);g.DrawEllipse(b,7,16,4,4);break;
            case 329: g.DrawEllipse(b,4,8,24,16);for(int i=0;i<6;i++){double t=i*Math.PI/3;g.DrawEllipse(c,16+11*(float)Math.Cos(t)-1.5f,16+8*(float)Math.Sin(t)-1.5f,3,3);}g.DrawEllipse(a,12,16,8,6);break;
            case 330: Lines(g,b,4,8,16,8,16,24);Lines(g,c,16,8,28,8);g.DrawLine(a,4,16,10,16);g.DrawLine(a,22,16,28,16);break;
            case 331: Lines(g,b,4,8,28,8);Lines(g,c,16,8,16,28);Arrow(g,a,16,28,16,20);g.DrawLine(b,4,16,10,16);g.DrawLine(b,22,16,28,16);break;
            case 332: g.DrawEllipse(b,5,8,22,16);g.DrawEllipse(c,10,11,12,10);Box(g,a,13,6,6,5);g.DrawLine(a,16,21,16,26);break;
            case 333: g.DrawEllipse(b,4,10,24,12);g.DrawEllipse(c,10,13,12,6);for(int i=0;i<4;i++)g.DrawLine(a,7+i*6,10,7+i*6,22);break;
            case 334: g.DrawEllipse(b,4,8,24,16);g.DrawEllipse(c,10,11,12,10);g.DrawEllipse(a,12,13,8,6);break;
            case 335: g.DrawBezier(b,4,24,12,10,20,26,28,14);g.DrawBezier(c,4,20,12,6,20,22,28,10);g.DrawEllipse(a,3,20,4,4);break;
            case 336: Box(g,b,4,10,10,12);Box(g,c,18,10,10,12);Box(g,a,12,13,8,6);g.DrawLine(a,14,16,18,16);break;
            // 材料与包装
            case 337: Box(g,b,4,10,12,12);Poly(g,c,16,10,28,10,24,22,20,22,20,16,16,16);g.DrawEllipse(a,10,16,3,3);break;
            case 338: Box(g,b,3,12,26,8);for(int i=0;i<3;i++){g.DrawEllipse(c,7+i*8,16,4,4);}g.DrawLine(a,3,12,29,12);break;
            case 339: Box(g,b,4,6,24,20);Box(g,c,10,10,12,12);for(int i=0;i<4;i++)g.DrawLine(a,4+i*7,6,4+i*7,26);break;
            case 340: Box(g,b,4,6,24,20);Lines(g,c,4,20,28,6);Lines(g,a,4,12,18,6);break;
            case 341: g.DrawArc(b,4,14,24,18,180,180);Lines(g,c,4,14,28,14);for(int i=0;i<3;i++)g.DrawArc(a,7+i*7,18,6,6,180,180);break;
            case 342: Box(g,b,4,8,24,18);for(int i=0;i<8;i++)g.DrawEllipse(c,6+(i%4)*6,12+(i/4)*7,3,3);g.DrawLine(a,4,8,28,8);break;
            case 343: Box(g,b,4,6,24,20);Lines(g,c,4,6,28,26);Lines(g,c,28,6,4,26);break;
            case 344: Box(g,b,4,6,24,20);Lines(g,c,10,6,10,26);Lines(g,c,22,6,22,26);g.DrawLine(a,10,16,22,16);break;
            case 345: Box(g,b,4,8,24,16);for(int i=0;i<10;i++)g.DrawEllipse(c,6+(i%5)*5,11+(i/5)*6,2,2);break;
            case 346: Box(g,b,4,10,24,12);for(int i=0;i<7;i++)g.DrawEllipse(c,6+i*3,13,3,3);g.DrawLine(a,4,22,28,22);break;
            case 347: g.DrawEllipse(b,6,8,20,16);g.DrawEllipse(c,12,8,8,16);g.DrawLine(a,6,16,12,16);Arrow(g,a,20,16,28,16);break;
            case 348: Box(g,b,3,6,26,20);for(int i=0;i<4;i++){g.DrawBezier(c,4,8+i*5,12,6+i*5,20,10+i*5,28,8+i*5);}break;
            case 349: Poly(g,b,4,12,10,6,20,8,28,6,26,26,6,26);g.DrawLine(c,10,6,12,26);g.DrawLine(a,20,8,18,26);break;
            case 350: Box(g,b,4,6,24,20);Lines(g,c,4,22,16,8);Lines(g,a,12,22,24,8);break;
            case 351: Page(g,b);Lines(g,c,10,12,22,12);Lines(g,c,10,17,22,17);g.DrawLine(a,4,26,28,26);break;
            case 352: Box(g,b,4,8,24,16);for(int i=0;i<8;i++)g.DrawEllipse(c,6+(i%4)*6,11+(i/4)*7,4,4);break;
            // 包装与清单
            case 353: Box(g,b,4,8,24,18);Lines(g,c,4,14,28,14);Lines(g,a,12,8,12,14);Lines(g,a,20,8,20,14);g.DrawLine(b,16,8,16,26);break;
            case 354: Box(g,b,4,4,24,24);for(int y=9;y<27;y+=6)g.DrawLine(c,7,y,25,y);g.DrawEllipse(a,9,12,3,3);g.DrawEllipse(a,9,18,3,3);break;
            case 355: Box(g,b,3,4,26,24);g.DrawLine(c,3,10,29,10);g.DrawLine(c,3,16,29,16);g.DrawLine(c,3,22,29,22);g.DrawLine(a,10,4,10,28);g.DrawLine(a,20,4,20,28);break;
            case 356: Box(g,b,4,8,24,16);for(int i=0;i<5;i++)g.DrawLine(c,7+i*5,12,7+i*5,20);g.DrawLine(a,4,24,28,24);break;
            case 357: Box(g,b,4,4,24,24);Lines(g,c,9,9,9,23);Lines(g,c,13,9,13,23);Lines(g,a,19,9,23,9);Lines(g,a,19,14,23,14);Lines(g,a,19,19,23,19);break;
            case 358: Box(g,b,4,6,24,20);using(var f3=new SolidBrush(Color.FromArgb(70,125,86,188)))g.FillEllipse(f3,8,10,16,12);g.DrawEllipse(c,8,10,16,12);break;
            case 359: Box(g,b,4,6,24,20);Arrow(g,c,7,16,25,16);g.DrawEllipse(a,10,16,3,3);g.DrawEllipse(a,18,16,3,3);break;
            case 360: Page(g,b);Box(g,c,9,12,14,10);g.DrawLine(a,12,15,20,15);g.DrawLine(a,12,19,17,19);Star(g,a,24,8,3f);break;
            case 361: Box(g,b,3,4,26,24);Box(g,c,7,8,18,16);Lines(g,a,7,8,18,24);g.DrawEllipse(a,12,14,6,6);break;
            case 362: Box(g,b,3,6,26,20);g.DrawLine(c,3,12,29,12);for(int y=16;y<25;y+=4)g.DrawLine(a,7,y,25,y);break;
            case 363: Box(g,b,3,18,26,10);Lines(g,c,7,18,7,28);Lines(g,a,7,23,25,23);g.DrawLine(b,3,18,29,18);break;
            case 364: Page(g,b);Lines(g,c,10,11,22,11);Lines(g,c,10,16,22,16);Lines(g,a,10,21,18,21);g.DrawEllipse(a,24,20,5,5);break;
            case 365: Lines(g,b,4,16,28,16);Lines(g,c,10,10,10,22);Lines(g,c,22,10,22,22);g.DrawEllipse(a,16,13,6,6);break;
            case 366: Lines(g,b,4,26,4,10,28,10);for(int i=0;i<4;i++)g.DrawLine(c,7+i*6,10,9+i*6,14);g.DrawLine(a,4,20,28,20);break;
            case 367: Box(g,b,4,6,24,20);Box(g,c,10,11,12,10);Lines(g,a,4,16,10,16);Lines(g,a,22,16,28,16);Arrow(g,b,16,6,16,11);break;
            case 368: Lines(g,b,4,20,14,20);Lines(g,c,14,12,14,28);Poly(g,a,18,12,26,12,22,20);g.DrawEllipse(a,8,18,3,3);break;
            // 图纸与供应链
            case 369: Lines(g,b,4,26,4,10,20,10,20,26);for(int i=0;i<4;i++)g.DrawArc(c,6+i*4,6,5,6,180,180);Arrow(g,a,24,14,24,22);break;
            case 370: Box(g,b,4,6,24,20);for(int i=0;i<6;i++)g.DrawLine(c,5+i*5,26,9+i*5,6);break;
            case 371: Box(g,b,3,10,20,14);Arrow(g,c,23,17,29,17);g.DrawEllipse(a,8,17,5,5);g.DrawLine(a,3,24,23,24);break;
            case 372: Box(g,b,4,12,18,12);g.DrawEllipse(c,20,12,9,9);g.DrawLine(a,23,12,23,21);g.DrawLine(a,20,15,29,15);break;
            case 373: Box(g,b,3,12,18,14);Arrow(g,c,21,19,29,19);g.DrawLine(a,3,19,21,19);Arrow(g,a,12,8,12,12);break;
            case 374: Box(g,b,11,12,18,14);Arrow(g,c,3,19,11,19);g.DrawLine(a,11,19,29,19);Arrow(g,a,20,8,20,12);break;
            case 375: for(int y=0;y<3;y++)for(int x=0;x<3;x++)Box(g,(x+y)%2==0?b:c,4+x*9,4+y*9,7,7);break;
            case 376: Box(g,b,4,8,24,18);Lines(g,c,9,14,13,18,20,10);g.DrawEllipse(a,22,20,5,5);break;
            case 377: Box(g,b,3,12,14,12);Arrow(g,c,17,18,29,18);g.DrawEllipse(a,6,8,5,5);g.DrawEllipse(a,12,8,5,5);break;
            case 378: Box(g,b,4,14,16,12);g.DrawEllipse(c,8,26,5,5);g.DrawEllipse(c,18,26,5,5);Arrow(g,a,20,8,28,8);break;
            case 379: Box(g,b,4,10,14,14);g.DrawEllipse(c,14,6,9,9);Arrow(g,a,18,24,28,24);g.DrawLine(a,4,17,18,17);break;
            case 380: Page(g,b);g.DrawEllipse(c,18,16,10,10);Lines(g,a,21,19,24,22,28,18);g.DrawLine(b,10,14,22,14);break;
            case 381: g.DrawEllipse(b,4,10,18,16);Lines(g,c,10,10,10,26);Lines(g,a,8,14,12,14);Lines(g,a,8,22,12,22);Arrow(g,a,22,18,29,18);break;
            case 382: Box(g,b,4,6,24,20);g.DrawEllipse(c,11,11,10,10);Lines(g,a,8,16,11,13,14,16,11,19);break;
            case 383: Page(g,b);Lines(g,c,10,10,22,10);Lines(g,c,10,15,18,15);Lines(g,a,10,20,22,20);Star(g,a,24,20,3.5f);break;
            case 384: g.DrawEllipse(b,4,4,24,24);Lines(g,c,16,8,16,16,22,16);Arrow(g,a,10,20,22,20);break;
            case 385: g.DrawEllipse(b,4,4,24,24);using(var f4=new SolidBrush(Color.FromArgb(110,b.Color)))g.FillPie(f4,4,4,24,24,270,90);Lines(g,a,4,26,28,26);break;
            case 386: Box(g,b,3,6,12,20);Box(g,c,17,6,12,20);Box(g,a,3,10,26,5);g.DrawLine(b,8,12,14,12);g.DrawLine(b,22,16,28,16);break;
        }
    }
}
