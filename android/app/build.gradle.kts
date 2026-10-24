import java.util.Properties
plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}
// Signing material lives OUTSIDE the source tree. Point to it with
//   BLUNTYBUDS_KEYSTORE_PROPS=/path/to/keystore.properties   (or -Pbluntybuds.keystoreProps=...)
// keystore.properties: storeFile (relative to the properties file), storePassword, keyAlias, keyPassword.
val ksFile: File? = (System.getenv("BLUNTYBUDS_KEYSTORE_PROPS") ?: findProperty("bluntybuds.keystoreProps") as String?)?.let { File(it) }?.takeIf { it.exists() }
val ksProps = Properties().apply { ksFile?.inputStream()?.use { load(it) } }
android {
    namespace = "io.github.arkivx0.bluntybuds"
    compileSdk = 35
    buildToolsVersion = "35.0.0"
    defaultConfig {
        applicationId = "io.github.arkivx0.bluntybuds"
        minSdk = 29
        targetSdk = 35
        versionCode = 13
        versionName = "1.4.4"
    }
    signingConfigs {
        if (ksFile != null) create("release") {
            storeFile = File(ksFile.parentFile, ksProps.getProperty("storeFile"))
            storePassword = ksProps.getProperty("storePassword")
            keyAlias = ksProps.getProperty("keyAlias")
            keyPassword = ksProps.getProperty("keyPassword")
        }
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("release")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    lint { abortOnError = false; checkReleaseBuilds = false }
}
dependencies {
    testImplementation("junit:junit:4.13.2")
}
