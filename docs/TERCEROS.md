# Componentes de terceros

La entrega incluye .NET y Windows Desktop Runtime, SQLite y los paquetes resueltos por NuGet. `release-manifest.json` identifica las versiones de dependencias de la aplicación. `Licenses/PACKAGE-NOTICES.txt` conserva identificador, versión, licencia declarada, autores y avisos de copyright extraídos de los metadatos públicos de cada paquete utilizado en la publicación.

`Licenses` conserva además los archivos LICENSE, NOTICE y THIRD-PARTY-NOTICES suministrados en la raíz de cada paquete, incluidos los runtimes. Se incluye el texto completo de [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0.txt) para las dependencias que declaran esa licencia. Los archivos de avisos forman parte del manifiesto y su comprobación de integridad.

SQLite declara su código principal como [dominio público](https://www.sqlite.org/copyright.html); sus envoltorios y paquetes NuGet mantienen sus propias licencias. Estos avisos no otorgan derechos sobre letras obtenidas de servicios externos ni cambian la licencia del código propio de Song Sense. La entrega no incluye un catálogo de letras ni las letras originales usadas en pruebas.
