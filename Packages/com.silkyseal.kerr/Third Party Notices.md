# Third Party Notices

The package ships two data sets made by others, and its code implements methods from the papers listed under Methods.

## Hipparcos catalogue: `Runtime/Data/Hipparcos.bytes`

ICRS positions, V magnitudes and B − V colours of 117,955 stars, packed by `StarCatalogue.Pack` from VizieR table
I/239/hip_main.

- ESA, 1997, *The Hipparcos and Tycho Catalogues*, ESA SP-1200.
- Retrieved through the VizieR catalogue service (CDS, Strasbourg, France; DOI 10.26093/cds/vizier) at its
  vizier.cfa.harvard.edu mirror. Ochsenbein, Bauer & Marcout 2000, A&AS 143, 23.

## Milky Way: `Runtime/Data/MilkyWay.exr`

`milkyway_2020_4k.exr` from *Deep Star Maps 2020*. It is NASA's full star map, 1.7 billion stars, with the Hipparcos
and Tycho stars (brighter than about V 11.5) left out, so the stars fainter than that remain as glow. It is in
celestial coordinates with RA 0 at the centre. Here it is resampled from 4096 × 2048 to 2048 × 1024 and stored as
half-float ZIP EXR.

- NASA/Goddard Space Flight Center Scientific Visualization Studio. Gaia DR2: ESA/Gaia/DPAC.
  <https://svs.gsfc.nasa.gov/4851>
- Public domain: the SVS states its content is in the public domain unless otherwise noted, and NASA asks to be
  acknowledged as the source. The NASA insignia and logotype are not covered, and the package uses neither.

## Methods

The code is written for this package. These are the methods it implements.

- Lensing renderer, star-map filtering and the camera's frame: James, von Tunzelmann, Franklin & Thorne 2015,
  *Gravitational lensing by spinning black holes in astrophysics, and in the movie Interstellar*, Class. Quantum Grav.
  32, 065001, arXiv:1502.03808.
- Thin accretion disk: Novikov & Thorne 1973, in *Black Holes (Les Astres Occlus)*, Gordon and Breach, 343; Page &
  Thorne 1974, ApJ 191, 499.
- Circular orbits and the ISCO: Bardeen, Press & Teukolsky 1972, ApJ 178, 347.
- Adaptive Runge–Kutta step: Cash & Karp 1990, ACM Trans. Math. Softw. 16, 201.
- CIE 1931 colour matching functions: Wyman, Sloan & Shirley 2013, J. Comput. Graph. Tech. 2(2), 1.
- Star temperature from B − V: Ballesteros 2012, EPL 97, 34008.
- Exposure: Lagarde & de Rousiers 2014, *Moving Frostbite to Physically Based Rendering 3.0*, SIGGRAPH course notes.

The tests check the renderer against these:

- Closed-form Kerr geodesics and the photon shell: Gralla & Lupsasca 2020, Phys. Rev. D 101, 044031 and 044032
  (arXiv:1910.12873, arXiv:1910.12881); Teo 2003, Gen. Relativ. Gravit. 35, 1909.
- Elliptic integrals: Carlson 1995, Numer. Algorithms 10, 13.
- Photon ring subrings: Johnson et al. 2020, Sci. Adv. 6, eaaz1310.
- M87* viewing geometry: Event Horizon Telescope Collaboration 2019, ApJL 875, L5.
