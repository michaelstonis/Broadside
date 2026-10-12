using Broadside.Graphics;

namespace Broadside.Tests.Content;

/// <summary>Transformation matrices as §8.3.3 and §8.3.4 define them: row vectors, premultiplication, inverses.</summary>
public sealed class MatrixTests
{
    [Fact]
    public void A_point_maps_as_a_row_vector()
    {
        // §8.3.4: x′ = a·x + c·y + e, y′ = b·x + d·y + f.
        var matrix = new Matrix(1, 2, 3, 4, 5, 6);

        Assert.Equal(new PathPoint(1 + 6 + 5, 2 + 8 + 6), matrix.Transform(1, 2));
        Assert.Equal(new PathPoint(1 + 6, 2 + 8), matrix.TransformVector(1, 2));
    }

    [Fact]
    public void The_product_applies_the_first_matrix_first()
    {
        // §8.3.4 NOTE 2: X_D = (X_S × M_S) × M_C = X_S × (M_S × M_C).
        Matrix scale = Matrix.CreateScale(2, 3);
        Matrix translate = Matrix.CreateTranslation(10, 20);

        Matrix product = scale * translate;

        Assert.Equal(new Matrix(2, 0, 0, 3, 10, 20), product);
        Assert.Equal(translate.Transform(scale.Transform(1, 1)), product.Transform(1, 1));
        Assert.Equal(product, Matrix.Multiply(scale, translate));
        Assert.NotEqual(product, translate * scale);
    }

    [Fact]
    public void An_invertible_matrix_has_an_inverse_that_undoes_it()
    {
        var matrix = new Matrix(0, 2, -3, 0, 7, 11);

        Assert.True(matrix.TryInvert(out Matrix inverse));
        Assert.True((matrix * inverse).IsIdentity);
        PathPoint back = inverse.Transform(matrix.Transform(4, 5));
        Assert.Equal(4, back.X, 1e-12);
        Assert.Equal(5, back.Y, 1e-12);
        Assert.Equal(6, matrix.Determinant);
    }

    [Fact]
    public void A_singular_matrix_has_no_inverse()
    {
        Assert.False(new Matrix(0, 0, 0, 0, 5, 5).TryInvert(out Matrix inverse));
        Assert.Equal(Matrix.Identity, inverse);
    }

    [Fact]
    public void Matrices_compare_by_value_and_print_as_a_pdf_array()
    {
        Assert.True(new Matrix(1, 0, 0, 1, 0, 0) == Matrix.Identity);
        Assert.False(Matrix.Identity != new Matrix(1, 0, 0, 1, 0, 0));
        Assert.Equal(Matrix.Identity.GetHashCode(), new Matrix(1, 0, 0, 1, 0, 0).GetHashCode());
        Assert.Equal("[1 0 0 1 0.5 -2]", new Matrix(1, 0, 0, 1, 0.5, -2).ToString());
        Assert.Equal("(1.5, 2)", new PathPoint(1.5, 2).ToString());
    }
}
