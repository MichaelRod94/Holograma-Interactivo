using Holograma3D.Core.Domain.Models;

namespace Holograma3D.Tests.Models;

public class ModelFileInspectorTests
{
    private static InspectionResult Inspect(byte[] bytes, string fileName) => ModelFileInspector.Inspect(new MemoryStream(bytes), fileName);

    private static void Rejects(byte[] bytes, string fileName) =>
        Assert.Throws<ModelValidationException>(() => Inspect(bytes, fileName));

    [Fact]
    public void Stl_binario_de_un_triangulo_es_valido_y_el_stream_queda_en_cero()
    {
        var stream = new MemoryStream(ModelFiles.BinaryStl(1));

        var result = ModelFileInspector.Inspect(stream, "Cubo.STL");

        Assert.Equal(new InspectionResult(ModelFormat.Stl, 1), result);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Stl_binario_cuya_cabecera_empieza_con_solid_es_binario() =>
        Assert.Equal(new InspectionResult(ModelFormat.Stl, 3), Inspect(ModelFiles.BinaryStl(3, header: "solid exportado por CAD"), "a.stl"));

    [Fact]
    public void Stl_binario_con_tamano_que_no_cuadra_falla() => Rejects([.. ModelFiles.BinaryStl(1), 0], "a.stl");

    [Fact]
    public void Stl_binario_sin_triangulos_falla() => Rejects(ModelFiles.BinaryStl(0), "a.stl");

    [Fact]
    public void Stl_ascii_cuenta_los_facets() =>
        Assert.Equal(new InspectionResult(ModelFormat.Stl, 2), Inspect(ModelFiles.AsciiStl(2), "a.stl"));

    [Fact]
    public void Stl_ascii_sin_endsolid_falla() => Rejects(ModelFiles.AsciiStl(2, withEnd: false), "a.stl");

    [Fact]
    public void Glb_minimo_es_valido() =>
        Assert.Equal(new InspectionResult(ModelFormat.Glb, null), Inspect(ModelFiles.Glb(), "a.glb"));

    [Fact]
    public void Glb_con_longitud_que_no_cuadra_falla() => Rejects([.. ModelFiles.Glb(), 0, 0, 0, 0], "a.glb");

    [Fact]
    public void Extension_que_no_coincide_falla() => Rejects(ModelFiles.BinaryStl(1), "a.glb");

    [Fact]
    public void Vacio_o_desconocido_falla()
    {
        Rejects([], "a.stl");
        Rejects("hola, no soy un modelo"u8.ToArray(), "a.stl");
    }
}
