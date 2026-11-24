# Third Party Notices

The package ships two data sets made by others.

## Hipparcos catalogue: `Runtime/Data/Hipparcos.bytes`

ICRS positions, V magnitudes and B − V colours of 117,955 stars, packed by `StarCatalogue.Pack` from VizieR table
I/239/hip_main.

- ESA, 1997, *The Hipparcos and Tycho Catalogues*, ESA SP-1200.
- Retrieved through the VizieR catalogue service (CDS, Strasbourg, France; DOI 10.26093/cds/vizier) at its
  vizier.cfa.harvard.edu mirror. Ochsenbein, Bauer & Marcout 2000, A&AS 143, 23.

## Milky Way: `Runtime/Data/MilkyWay.exr`

The Milky Way glow, with catalogue stars removed, from *Deep Star Maps 2020* (`milkyway_2020_4k.exr`). It is in
celestial coordinates with RA 0 at the centre, resampled here from 4096 × 2048 to 2048 × 1024 and stored as half-float
ZIP EXR.

- NASA/Goddard Space Flight Center Scientific Visualization Studio, <https://svs.gsfc.nasa.gov/4851>.
- Public domain under NASA's media usage guidelines, with the credit above.
