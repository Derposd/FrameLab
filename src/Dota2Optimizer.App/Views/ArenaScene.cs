using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Dota2Optimizer.App.Views;

/// <summary>Local procedural assets. Seed, camera route and animation phases are fixed for each recorded run.</summary>
public sealed class ArenaScene
{
    public Model3DGroup World { get; } = new();
    public PerspectiveCamera Camera { get; } = new() { FieldOfView = 62, NearPlaneDistance = .15, FarPlaneDistance = 240 };
    private readonly List<(TranslateTransform3D Transform, double Offset, int Direction)> _cars = new();
    private readonly List<(TranslateTransform3D Transform, double Offset)> _rain = new();
    private readonly List<(TranslateTransform3D Transform, double Offset)> _steam = new();
    private readonly Dictionary<string, Material> _materials = new();

    public ArenaScene()
    {
        World.Children.Add(new AmbientLight(Color.FromRgb(100, 111, 140)));
        World.Children.Add(new DirectionalLight(Color.FromRgb(151, 172, 216), new Vector3D(-.5,-1,-.4)));
        World.Children.Add(new DirectionalLight(Color.FromRgb(244, 134, 78), new Vector3D(.7,-.3,.4)));
        var city = new Model3DGroup();
        Box(city, 0,-.3,0, 100,.4,190,"#101926");
        Box(city,0,-.06,0, 13,.16,170,"#151F2E");
        foreach (var side in new[] {-1,1})
        {
            Box(city,side*8.2,.02,0, 3,.3,170,"#374254");
            Box(city,side*6.6,.12,0, .12,.15,170,"#77ADB9",true);
            for(var z=-80;z<80;z+=6) Box(city,side*3.2,.04,z,.12,.02,2.8,"#8899A7");
        }
        var random = new Random(481516);
        for(var side=-1;side<=1;side+=2)
        for(var i=0;i<15;i++)
        {
            double z=i*11-80, h=9+random.Next(19), x=side*(12+random.NextDouble()*2), w=5+random.NextDouble()*3;
            string facade=new[]{"#27334A","#323949","#344C5D","#252C3F"}[i%4];
            Box(city,x,h/2,z,w,h,8,facade);
            Box(city,x,h+.2,z,w+.3,.45,8.3,"#151C29");
            Box(city,x,h+1,z,2,1.5,2,"#4A5364");
            for(var y=2.5;y<h-1;y+=2.5)
            for(var zz=-2.7;zz<3;zz+=1.8)
            {
                var lit=random.NextDouble()>.28;
                Box(city,x-side*(w/2+.03),y,z+zz,.06,1.25,.85,lit?(i%3==0?"#ECA76F":"#77B4C9"):"#111E30",lit);
            }
            // Storefronts, canopies, vertical neon strips and their stylised wet-road reflections.
            string neon=i%3==0?"#FF693D":i%3==1?"#63C9D4":"#B38BDC";
            Box(city,x-side*(w/2+.1),1.6,z,.12,1.5,5.5,"#61A9B9",true);
            Box(city,x-side*(w/2+.7),3,z,1.4,.25,6,"#151E2B");
            Box(city,x-side*(w/2+.2),4.7,z+3,.15,4,.28,neon,true);
            for(int r=0;r<7;r++) Box(city,side*(4+r*.17),.06,z+3+r*.3,.6,.015,.10+r*.02,neon,true,.14);
            // Dense skyline behind the street.
            var bh=h+6+random.Next(15);
            Box(city,side*(23+random.Next(10)),bh/2,z+4,8,bh,9,"#202D42");
        }
        for(var z=-70;z<80;z+=15)
        foreach(var side in new[]{-1,1})
        {
            Box(city,side*7.1,3,z,.12,6,.12,"#465468");
            Box(city,side*6.3,6,z,1.7,.12,.12,"#465468");
            Box(city,side*5.6,5.9,z,.7,.08,.4,"#FFE0AE",true);
            Box(city,side*4.6,.07,z,2.1,.018,3.3,"#D8B78B",true,.09);
            Box(city,side*9,.65,z+4,.4,1.3,.4,"#A9583A");
        }
        // Elevated transit bridges frame the moving camera.
        foreach(var z in new[]{-38,12,62})
        {
            Box(city,0,8,z,28,.7,2.2,"#25344A");
            Box(city,0,8.6,z-1,27,.08,.08,"#FF754A",true);
            for(var x=-12;x<=12;x+=2)Box(city,x,8.9,z-1,.08,1,.08,"#6B7E97");
        }
        city.Freeze(); World.Children.Add(city);
        for(var i=0;i<12;i++)
        {
            var car=new Model3DGroup();int direction=i%2==0?1:-1;
            Box(car,0,.6,0,1.65,.55,3.5,new[]{"#BF5B3F","#426B7D","#BEB7A4"}[i%3]);
            Box(car,0,1.05,-.15,1.35,.45,1.8,"#162B41");
            foreach(var x in new[]{-.62,.62})
            {
                Box(car,x,.65,1.78,.32,.15,.04,"#FFE7C0",true);
                Box(car,x,.65,-1.78,.34,.12,.04,"#FF4427",true);
                Box(car,x,.05,2.8,.35,.025,2.3,"#EFD6B1",true,.12);
            }
            var t=new TranslateTransform3D(); car.Transform=t;World.Children.Add(car);_cars.Add((t,i*13.8,direction));
        }
        // Rain is batched in eight moving volumes, keeping animation deterministic and allocations out of rendering.
        for(int group=0;group<8;group++)
        {
            var rain=new Model3DGroup();
            for(int j=0;j<60;j++)Box(rain,random.NextDouble()*30-15,random.NextDouble()*17,random.NextDouble()*150-75,.015,.6,.015,"#92B1CA",true,.28);
            var t=new TranslateTransform3D();rain.Transform=t;World.Children.Add(rain);_rain.Add((t,group*2.12));
        }
        for(int i=0;i<12;i++)
        {
            var smoke=new Model3DGroup();
            for(int j=0;j<4;j++)Box(smoke,Math.Sin(j)*.25,j*.35,0,.7+j*.2,.55,.6+j*.2,"#9BA9BA",true,.035);
            var t=new TranslateTransform3D();smoke.Transform=t;World.Children.Add(smoke);_steam.Add((t,i*1.7));
        }
        Update(0);
    }

    public string Chapter(double seconds) => seconds<10?"01 / НЕОНОВАЯ УЛИЦА":seconds<20?"02 / ПОТОК ТРАНСПОРТА":"03 / ДОЖДЬ И СВЕТ";
    public void Update(double seconds)
    {
        var t=seconds%30;
        var z=55-t*3.8; var x=Math.Sin(t*.18)*2.4;
        Camera.Position=new Point3D(x,3.1+Math.Sin(t*.12)*.7,z);
        Camera.LookDirection=new Vector3D(-x*.35,.4,-14);
        Camera.UpDirection=new Vector3D(0,1,0);
        foreach(var (tr,offset,dir) in _cars){tr.OffsetX=dir*2.2;tr.OffsetZ=((seconds*6*dir+offset+500)%145)-72;}
        foreach(var (tr,offset) in _rain)tr.OffsetY=-((seconds*12+offset)%17);
        foreach(var (tr,offset) in _steam){tr.OffsetX=7.2+Math.Sin(seconds+offset)*.3;tr.OffsetZ=offset*6-35;tr.OffsetY=(seconds*.6+offset)%3;}
    }

    private void Box(Model3DGroup group,double x,double y,double z,double w,double h,double d,string hex,bool emissive=false,double opacity=1)
    {
        var key=$"{hex}:{emissive}:{opacity}";
        if(!_materials.TryGetValue(key,out var mat))
        {
            var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)){Opacity=opacity};brush.Freeze();
            mat=emissive?new EmissiveMaterial(brush):new DiffuseMaterial(brush);mat.Freeze();_materials[key]=mat;
        }
        var mesh=new MeshGeometry3D();
        foreach(var p in new[]{new Point3D(x-w/2,y-h/2,z-d/2),new Point3D(x+w/2,y-h/2,z-d/2),new Point3D(x+w/2,y+h/2,z-d/2),new Point3D(x-w/2,y+h/2,z-d/2),new Point3D(x-w/2,y-h/2,z+d/2),new Point3D(x+w/2,y-h/2,z+d/2),new Point3D(x+w/2,y+h/2,z+d/2),new Point3D(x-w/2,y+h/2,z+d/2)})mesh.Positions.Add(p);
        foreach(var i in new[]{0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4})mesh.TriangleIndices.Add(i);
        mesh.Freeze();var model=new GeometryModel3D(mesh,mat);model.Freeze();group.Children.Add(model);
    }
}
