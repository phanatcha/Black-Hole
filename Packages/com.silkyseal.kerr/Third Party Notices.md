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
- Hot accretion flow around Sgr A*: Yuan, Quataert & Narayan 2003, ApJ 598, 301; Broderick & Loeb 2006, MNRAS 367,
  905; field strength from Broderick, Fish, Doeleman & Loeb 2011, ApJ 735, 110.
- Synchrotron emission and absorption: Dexter 2016, MNRAS 462, 115; radiative transfer: Rybicki & Lightman 1979,
  *Radiative Processes in Astrophysics*, Wiley.
- Gas inside the ISCO: Mummery & Balbus 2022, Phys. Rev. Lett. 129, 161101.
- Polarization carried along the ray: Walker & Penrose 1970, Commun. Math. Phys. 18, 265; Himwich, Johnson, Lupsasca &
  Strominger 2020, Phys. Rev. D 101, 084020.
- Radio false colour: matplotlib's afmhot colour map (Hunter 2007, Comput. Sci. Eng. 9, 90), the one EHT images use.
- Orbiting streaks: the "swirl space" Voronoi pattern described in the r/GraphicsProgramming post *WIP Kerr Black Hole
  render engine written in VEX*, https://www.reddit.com/r/GraphicsProgramming/comments/175lwfv/. Written here from the
  post's description; its code was not used.
- Gradient noise: Perlin 2002, *Improving Noise*, ACM Trans. Graph. 21(3), 681. Integer hash: O'Neill 2014, *PCG*,
  Harvey Mudd College HMC-CS-2014-0905, as chosen by Jarzynski & Olano 2020, J. Comput. Graph. Tech. 9(3), 21.
- The plunge and its two scripted falls: NASA Scientific Visualization Studio 14576, Schnittman & Powell 2024,
  https://svs.gsfc.nasa.gov/14576. Written here from its description; no code or frames were used.
- Zoom-whirl orbits: Levin & Perez-Giz 2008, Phys. Rev. D 77, 103005.

The tests check the renderer against these:

- Closed-form Kerr geodesics and the photon shell: Gralla & Lupsasca 2020, Phys. Rev. D 101, 044031 and 044032
  (arXiv:1910.12873, arXiv:1910.12881); Teo 2003, Gen. Relativ. Gravit. 35, 1909.
- Elliptic integrals: Carlson 1995, Numer. Algorithms 10, 13.
- Photon ring subrings: Johnson et al. 2020, Sci. Adv. 6, eaaz1310.
- M87* viewing geometry: Event Horizon Telescope Collaboration 2019, ApJL 875, L5.
- Sgr A* ring size: Event Horizon Telescope Collaboration 2022, ApJL 930, L12.
- Polarization twist β2: Palumbo, Wong & Prather 2020, ApJ 894, 156.
- Falling from rest far away (the rain frame): Hamilton & Lisle 2008, Am. J. Phys. 76, 519.
