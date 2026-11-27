namespace SilkySeal.Kerr
{
    /// <summary>
    /// The colour of a smooth spectrum known at only three optical frequencies, so a ray traced through glowing gas can
    /// be coloured without tracing every wavelength. The spectrum is taken as the quadratic in wavelength through the
    /// three samples and weighted by the CIE 1931 observer; that makes colour a fixed 3×3 matrix of the samples. Within
    /// 1.1 % of the luminance for blackbodies from 2000 K up and for power laws I_ν ∝ ν^(−3…2).
    /// </summary>
    public static class VisibleBand
    {
        // Grid-searched in 5 nm steps for the smallest worst error on those spectra (450/550/650 nm is 16 % off).
        const double BlueNanometres = 445, GreenNanometres = 535, RedNanometres = 615;
        const int FirstNanometre = 360, LastNanometre = 830;

        /// <summary>The sample frequencies in Hz: blue, green, red.</summary>
        public static readonly Vector3d Frequencies = new Vector3d(Hz(BlueNanometres), Hz(GreenNanometres), Hz(RedNanometres));

        static readonly Vector3d[] s_Rows = BuildRows();

        /// <summary>
        /// Linear sRGB in cd/m² from specific intensities I_ν at <see cref="Frequencies"/>, in cgs
        /// (erg s⁻¹ cm⁻² Hz⁻¹ sr⁻¹, what <see cref="Synchrotron"/> emits). Unclipped: a spectrum too saturated for
        /// sRGB comes back with a negative channel.
        /// </summary>
        public static Vector3d LinearSrgb(Vector3d samples) => new Vector3d(
            Vector3d.Dot(s_Rows[0], samples), Vector3d.Dot(s_Rows[1], samples), Vector3d.Dot(s_Rows[2], samples));

        /// <summary>The matrix rows behind <see cref="LinearSrgb"/>, for the GPU copy.</summary>
        internal static Vector3d[] Rows => s_Rows;

        static double Hz(double nm) => Blackbody.LightSpeed / (nm * 1e-9);

        static Vector3d[] BuildRows()
        {
            // XYZ_i = 683 ∫ I_λ m̄_i dλ with I_λ = I_ν c/λ², I_ν(λ) = Σ_j L_j(λ) sample_j (Lagrange basis in λ).
            // 1e-3 turns cgs I_ν into W m⁻² Hz⁻¹ sr⁻¹; 1e-9 is the 1 nm bin in metres.
            double x0 = 0, x1 = 0, x2 = 0, y0 = 0, y1 = 0, y2 = 0, z0 = 0, z1 = 0, z2 = 0;
            for (int nm = FirstNanometre; nm <= LastNanometre; nm++)
            {
                double metres = nm * 1e-9;
                double weight = Blackbody.LuminousEfficacy * 1e-3 * Blackbody.LightSpeed / (metres * metres) * 1e-9;
                double l0 = (nm - GreenNanometres) * (nm - RedNanometres) / ((BlueNanometres - GreenNanometres) * (BlueNanometres - RedNanometres));
                double l1 = (nm - BlueNanometres) * (nm - RedNanometres) / ((GreenNanometres - BlueNanometres) * (GreenNanometres - RedNanometres));
                double l2 = (nm - BlueNanometres) * (nm - GreenNanometres) / ((RedNanometres - BlueNanometres) * (RedNanometres - GreenNanometres));
                double mx = weight * Blackbody.MatchX(nm), my = weight * Blackbody.MatchY(nm), mz = weight * Blackbody.MatchZ(nm);
                x0 += mx * l0; x1 += mx * l1; x2 += mx * l2;
                y0 += my * l0; y1 += my * l1; y2 += my * l2;
                z0 += mz * l0; z1 += mz * l1; z2 += mz * l2;
            }
            // Columns of XYZ per sample, turned into linear sRGB.
            var c0 = Blackbody.XyzToLinearSrgb(new Vector3d(x0, y0, z0));
            var c1 = Blackbody.XyzToLinearSrgb(new Vector3d(x1, y1, z1));
            var c2 = Blackbody.XyzToLinearSrgb(new Vector3d(x2, y2, z2));
            return new[] { new Vector3d(c0.X, c1.X, c2.X), new Vector3d(c0.Y, c1.Y, c2.Y), new Vector3d(c0.Z, c1.Z, c2.Z) };
        }
    }
}
