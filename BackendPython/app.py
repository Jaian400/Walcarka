from flask import Flask, jsonify, send_from_directory, url_for, request, render_template
import os
import generate_plots
import config

app = Flask(__name__)

@app.route('/api/plots', methods=['GET'])
def get_image():
    filename = request.args.get('filename') 

    return send_from_directory(config.IMAGE_FOLDER, filename)

@app.route('/upload')
def upload():
    return render_template("upload.html")

@app.route('/images')
def images():
    return render_template('images.html')

@app.route('/')
def index():
    return render_template("index.html")

if __name__=='__main__':
    app.run(host='0.0.0.0', debug=True)