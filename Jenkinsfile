pipeline {
    //agent any
    agent {
        docker 'mcr.microsoft.com/dotnet/sdk:3.1'
    }
    environment {
        COMMON_SCRIPTS_DIR = "${env.WORKSPACE}/../common_ci-scripts"
    }
    stages {
        stage('Checkout') {
            steps {
                sh 'git fetch --tags'
                dir("${env.COMMON_SCRIPTS_DIR}") {
                    git "https://github.com/clarkx86/common_ci-scripts.git"
                    sh 'chmod +x *'
                }
            }
        }
        stage('Build') {
            environment {
                DOTNET_CLI_HOME = "/tmp/dotnet_cli_home"
                // Get plain repository name
                REPO_NAME = sh(returnStdout: true, script: "echo -n ${env.JOB_NAME} | sed -r 's/\\/.+//'")
                // Get latest tag and format clean version tag (and suffix) (e.g. "v1.3.1-beta" to "1.3.1" and "beta")
                TAG_LATEST = sh(returnStdout: true, script: "git tag --sort version:refname | tail -1").trim()
                TAG_CLEAN = sh(returnStdout: true, script: "echo -n \"${TAG_LATEST}\" | sed -r 's/^v//' | sed -r 's/\\-\\w+//'")
                TAG_SUFFIX = sh(returnStdout: true, script: "echo -n \"${TAG_LATEST}\" | sed -r 's/.+\\-//'")
                // Format assembly version (e.g. "1.3.123.1", as .NET expects MAJOR.MINOR.BUILD.RELEASE)
                ASSEMBLY_VERSION = sh(returnStdout: true, script: "${env.COMMON_SCRIPTS_DIR}/format_assembly-version.sh \"${TAG_CLEAN}\" \"${env.BUILD_NUMBER}\"")
            }
            matrix {
                //agent {
                    //docker 'mcr.microsoft.com/dotnet/sdk:3.1'
                //}
                axes {
                    axis {
                        name 'RUNTIME'
                        values 'linux-x64', 'win-x64'
                    }
                    axis {
                        name 'SELF_CONTAINED'
                        values 'true', 'false'
                    }
                }
                stages {
                    stage('Build') {
                        steps {
                            dir('src') {
                                sh 'dotnet publish vellum.csproj -c Release -r $RUNTIME \
                                    /property:Version=${ASSEMBLY_VERSION} \
                                    /p:PublishTrimmed=${SELF_CONTAINED} --self-contained $SELF_CONTAINED \
                                    /property:OutputType=Exe $(if [ ! -z $TAG_SUFFIX ]; then echo "/p:DefineConstants=\"$(echo $TAG_SUFFIX | tr [:lower:] [:upper:])\""; fi)'
                            }
                        }
                    }
                    stage('Package') {
                        //when { buildingTag() }
                        environment {
                            ARTIFACT_SUFFIX = sh(returnStdout: true, script: 'if [ "$SELF_CONTAINED" = "true" ]; then echo -n "-bundled"; elif [ "$OUTPUT_TYPE" = "Library" ]; then echo -n "-lib"; fi')
                            ARTIFACT_NAME = "${env.REPO_NAME}_${env.RUNTIME}${ARTIFACT_SUFFIX}_${env.TAG_LATEST}-${env.BUILD_NUMBER}"
                        }
                        steps {
                            dir('dist') {
                                script {
                                    zip(zipFile: "${env.ARTIFACT_NAME}.zip", dir: "../src/bin/Release/netcoreapp3.1/${env.RUNTIME}/publish", archive: true)
                                    sh 'sha256sum ${ARTIFACT_NAME}.zip >> checksums.sha256'
                                    archiveArtifacts "checksums.sha256"
                                }
                            }
                        }
                    }
                }
            }
        }
        stage('Deploy') {
            //when { buildingTag() }
            steps {
                // Deploy to GitHub Releases
                echo "Deploying artifacts to GitHub Releases..."
                sh 'ls -lhA dist'
            }
        }
    }
}